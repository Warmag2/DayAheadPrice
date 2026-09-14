using DayAheadPrice.Entities;
using DayAheadPrice.Enums;
using DayAheadPrice.Extensions;
using DayAheadPrice.Options;
using DayAheadPrice.Repositories;
using Microsoft.Extensions.Options;

namespace DayAheadPrice.Logic;

/// <summary>
/// Reads stored price slots and resamples them to the resolution of a requested zoom window.
/// </summary>
internal class PriceSeriesService
{
    private const int LiveHoursBefore = 12;

    private readonly PricePointRepository _repository;
    private readonly PriceContainer _container;
    private readonly EndpointOptions _endpointOptions;

    // Raw (pre-margin/VAT) time-weighted averages for fully-covered, wholly-past bars, keyed by domain + resolution
    // + local bar start. Such periods are immutable, so re-reads of coarse windows avoid rescanning thousands of raw
    // rows. Cleared implicitly only by process restart.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<AggregateKey, decimal> _aggregateCache = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="PriceSeriesService"/> class.
    /// </summary>
    /// <param name="repository">The price repository.</param>
    /// <param name="container">The price container, used to backfill missing history from the API.</param>
    /// <param name="endpointOptions">The endpoint options, supplying the domain to read.</param>
    public PriceSeriesService(PricePointRepository repository, PriceContainer container, IOptions<EndpointOptions> endpointOptions)
    {
        _repository = repository;
        _container = container;
        _endpointOptions = endpointOptions.Value;
    }

    /// <summary>
    /// Render a window of bars for the given navigation state.
    /// </summary>
    /// <param name="state">The requested zoom and position.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The rendered view, with no bars when nothing is stored for the domain.</returns>
    public async Task<PriceView> RenderAsync(PriceViewState state, CancellationToken cancellationToken = default)
    {
        var domain = _endpointOptions.Domain;
        var level = ZoomLevels.All[state.ZoomIndex];
        var isLive = state.ZoomIndex == ZoomLevels.FinestIndex && state.AnchorLocal == null;

        // For a positioned (non-live) view, make sure the history it wants is stored, fetching it from the API when
        // the user has panned or zoomed back past what we hold. A little extra is fetched beyond the window edge so
        // that panning further left keeps revealing older data rather than stopping at the first gap.
        if (!isLive)
        {
            var wantFrom = (state.AnchorLocal ?? DateTime.Now).Floor(level.AlignUnit);
            var wantTo = level.Span.AddTo(wantFrom);

            await _container.EnsureRangeAsync(
                level.Span.SubtractFrom(wantFrom).ToUniversalTime(),
                wantTo.ToUniversalTime(),
                cancellationToken);
        }

        var bounds = await _repository.GetBoundsAsync(domain, cancellationToken);

        if (bounds == null)
        {
            return new PriceView
            {
                Resolution = level.Resolution,
                HeaderUnit = level.HeaderUnit,
                FromLocal = DateTime.Now.Floor(CalendarUnit.Quarter),
                ToLocal = DateTime.Now.Floor(CalendarUnit.Quarter),
                ZoomIndex = state.ZoomIndex,
                IsLive = isLive
            };
        }

        var minLocal = bounds.Value.MinUtc.ToLocalTime();
        var maxLocal = bounds.Value.MaxUtc.ToLocalTime();

        DateTime fromLocal;
        DateTime toLocal;

        if (isLive)
        {
            fromLocal = DateTime.Now.Floor(CalendarUnit.Quarter).AddHours(-LiveHoursBefore);
            toLocal = maxLocal > fromLocal ? maxLocal : fromLocal.AddDays(1);
        }
        else
        {
            var anchor = state.AnchorLocal ?? DateTime.Now;
            fromLocal = anchor.Floor(level.AlignUnit);

            var earliestStart = minLocal.Floor(level.AlignUnit);
            var latestStart = maxLocal.Floor(level.AlignUnit);

            if (fromLocal < earliestStart)
            {
                fromLocal = earliestStart;
            }
            else if (fromLocal > latestStart)
            {
                fromLocal = latestStart;
            }

            toLocal = level.Span.AddTo(fromLocal);
        }

        var bars = await ResampleWindowAsync(domain, level.Resolution, fromLocal, toLocal, cancellationToken);

        return new PriceView
        {
            Bars = bars,
            Resolution = level.Resolution,
            HeaderUnit = level.HeaderUnit,
            FromLocal = fromLocal,
            ToLocal = toLocal,
            ZoomIndex = state.ZoomIndex,
            IsLive = isLive,
            CanPanLeft = fromLocal > minLocal,
            CanPanRight = toLocal < maxLocal
        };
    }

    /// <summary>
    /// The state one pan step earlier than the current view: the window start moves to the previous alignment
    /// boundary (the previous midnight, or the previous month at the coarsest scale).
    /// </summary>
    /// <param name="view">The current view.</param>
    /// <returns>The panned state.</returns>
    public static PriceViewState PanLeft(PriceView view)
    {
        var level = ZoomLevels.All[view.ZoomIndex];

        return new PriceViewState(view.ZoomIndex, PreviousBoundary(view.FromLocal, level.AlignUnit));
    }

    /// <summary>
    /// The state one pan step later than the current view: the window start moves to the next alignment boundary
    /// (the next midnight, or the next month at the coarsest scale), snapping back to live once it reaches the
    /// present.
    /// </summary>
    /// <param name="view">The current view.</param>
    /// <returns>The panned state.</returns>
    public static PriceViewState PanRight(PriceView view)
    {
        var level = ZoomLevels.All[view.ZoomIndex];
        var newFrom = NextBoundary(view.FromLocal, level.AlignUnit);

        if (view.ZoomIndex == ZoomLevels.FinestIndex && newFrom >= DateTime.Now)
        {
            return PriceViewState.Live;
        }

        return new PriceViewState(view.ZoomIndex, newFrom);
    }

    /// <summary>
    /// The state one zoom level finer, keeping the window centred where it is.
    /// </summary>
    /// <param name="view">The current view.</param>
    /// <returns>The zoomed state, or the current state when already at the finest level.</returns>
    public static PriceViewState ZoomIn(PriceView view)
    {
        return view.ZoomIndex.CanZoomIn() ? Recentre(view, view.ZoomIndex - 1) : StateOf(view);
    }

    /// <summary>
    /// The state one zoom level coarser, keeping the window centred where it is.
    /// </summary>
    /// <param name="view">The current view.</param>
    /// <returns>The zoomed state, or the current state when already at the coarsest level.</returns>
    public static PriceViewState ZoomOut(PriceView view)
    {
        return view.ZoomIndex.CanZoomOut() ? Recentre(view, view.ZoomIndex + 1) : StateOf(view);
    }

    private static PriceViewState StateOf(PriceView view)
    {
        return new PriceViewState(view.ZoomIndex, view.IsLive ? null : view.FromLocal);
    }

    /// <summary>
    /// The alignment boundary strictly before the given instant.
    /// </summary>
    /// <param name="local">The local instant.</param>
    /// <param name="unit">The alignment unit.</param>
    /// <returns>The previous boundary.</returns>
    private static DateTime PreviousBoundary(DateTime local, CalendarUnit unit)
    {
        var floored = local.Floor(unit);

        return floored < local ? floored : AddUnit(floored, unit, -1);
    }

    /// <summary>
    /// The alignment boundary strictly after the given instant.
    /// </summary>
    /// <param name="local">The local instant.</param>
    /// <param name="unit">The alignment unit.</param>
    /// <returns>The next boundary.</returns>
    private static DateTime NextBoundary(DateTime local, CalendarUnit unit)
    {
        return AddUnit(local.Floor(unit), unit, 1);
    }

    /// <summary>
    /// Add a whole number of the given calendar units to an instant.
    /// </summary>
    /// <param name="local">The local instant.</param>
    /// <param name="unit">The calendar unit to step by.</param>
    /// <param name="count">The number of units, which may be negative.</param>
    /// <returns>The shifted instant.</returns>
    private static DateTime AddUnit(DateTime local, CalendarUnit unit, int count)
    {
        return unit switch
        {
            CalendarUnit.Minute => local.AddMinutes(count),
            CalendarUnit.Quarter => local.AddMinutes(15 * count),
            CalendarUnit.Hour => local.AddHours(count),
            CalendarUnit.Month => local.AddMonths(count),
            CalendarUnit.Year => local.AddYears(count),
            _ => local.AddDays(count),
        };
    }

    private static PriceViewState Recentre(PriceView view, int newZoomIndex)
    {
        var centre = view.FromLocal + TimeSpan.FromTicks((view.ToLocal - view.FromLocal).Ticks / 2);
        var newSpan = ZoomLevels.All[newZoomIndex].Span;

        return new PriceViewState(newZoomIndex, centre - TimeSpan.FromTicks(newSpan.Approximate.Ticks / 2));
    }

    /// <summary>
    /// Compute the contiguous time ranges within <paramref name="fromUtc"/>..<paramref name="toUtc"/> that no
    /// stored slot covers.
    /// </summary>
    /// <param name="slots">The stored slots overlapping the window.</param>
    /// <param name="fromUtc">The inclusive window start, UTC.</param>
    /// <param name="toUtc">The exclusive window end, UTC.</param>
    /// <returns>The missing ranges, ordered, each an inclusive start / exclusive end in UTC.</returns>
    private static List<(DateTime FromUtc, DateTime ToUtc)> ComputeGaps(
        IReadOnlyCollection<PricePoint> slots,
        DateTime fromUtc,
        DateTime toUtc)
    {
        var gaps = new List<(DateTime FromUtc, DateTime ToUtc)>();
        var cursor = fromUtc;

        var ordered = slots
            .Where(s => s.DateBegin.HasValue && s.DateEnd.HasValue)
            .Select(s => (Start: s.DateBegin!.Value, End: s.DateEnd!.Value))
            .OrderBy(s => s.Start);

        foreach (var slot in ordered)
        {
            if (slot.Start > cursor)
            {
                gaps.Add((cursor, slot.Start < toUtc ? slot.Start : toUtc));
            }

            if (slot.End > cursor)
            {
                cursor = slot.End;
            }

            if (cursor >= toUtc)
            {
                return gaps;
            }
        }

        if (cursor < toUtc)
        {
            gaps.Add((cursor, toUtc));
        }

        return gaps;
    }

    /// <summary>
    /// Produce the bars for a window, serving fully-covered past bars from the in-memory aggregate cache and reading
    /// the database only for the span of bars still uncached. Any real holes inside the read span are filled from the
    /// API before resampling.
    /// </summary>
    /// <param name="domain">The price domain.</param>
    /// <param name="resolution">The width of one bar.</param>
    /// <param name="fromLocal">The inclusive local window start.</param>
    /// <param name="toLocal">The exclusive local window end.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The bars, keyed by local start, omitting any bar with no data.</returns>
    private async Task<SortedList<DateTime, decimal>> ResampleWindowAsync(
        string domain,
        CalendarStep resolution,
        DateTime fromLocal,
        DateTime toLocal,
        CancellationToken cancellationToken)
    {
        var bars = new SortedList<DateTime, decimal>();
        var uncached = new List<DateTime>();

        for (var barStart = fromLocal; barStart < toLocal; barStart = resolution.AddTo(barStart))
        {
            if (_aggregateCache.TryGetValue(new AggregateKey(domain, resolution.Unit, resolution.Count, barStart), out var cached))
            {
                bars[barStart] = cached;
            }
            else
            {
                uncached.Add(barStart);
            }
        }

        if (uncached.Count == 0)
        {
            return bars;
        }

        // Read (and gap-fill) only the contiguous span covering the bars we could not serve from cache.
        var readFromLocal = uncached[0];
        var readToLocal = resolution.AddTo(uncached[^1]);
        var readFromUtc = readFromLocal.ToUniversalTime();
        var readToUtc = readToLocal.ToUniversalTime();

        var slots = await _repository.GetRangeAsync(domain, readFromUtc, readToUtc, cancellationToken);

        // Fill any real holes inside the read span (e.g. a period nobody looked at) from the API, then re-read so the
        // freshly fetched slots are rendered. FillGapsAsync only fetches past ranges and throttles repeats, so a
        // healthy span (no gaps) costs nothing here.
        var gaps = ComputeGaps(slots, readFromUtc, readToUtc);

        if (gaps.Count > 0 && await _container.FillGapsAsync(gaps, cancellationToken))
        {
            slots = await _repository.GetRangeAsync(domain, readFromUtc, readToUtc, cancellationToken);
        }

        var intervals = slots
            .Where(s => s.DateBegin.HasValue && s.DateEnd.HasValue)
            .Select(s => (Start: s.DateBegin!.Value.ToLocalTime(), End: s.DateEnd!.Value.ToLocalTime(), s.Price))
            .OrderBy(i => i.Start)
            .ToList();

        var nowUtc = DateTime.UtcNow;

        foreach (var barStart in uncached)
        {
            var barEnd = resolution.AddTo(barStart);
            var (value, totalTicks) = ComputeBar(intervals, barStart, barEnd);

            if (totalTicks <= 0)
            {
                continue;
            }

            bars[barStart] = value;

            // Cache only a fully-covered bar that lies wholly in the past; such a period can no longer change, so a
            // later re-read need not rescan its raw rows. A partially-covered or still-open bar is left uncached.
            var fullyCovered = totalTicks == (barEnd - barStart).Ticks;

            if (fullyCovered && barEnd.ToUniversalTime() <= nowUtc)
            {
                _aggregateCache[new AggregateKey(domain, resolution.Unit, resolution.Count, barStart)] = value;
            }
        }

        return bars;
    }

    /// <summary>
    /// Time-weighted average of the overlapping intervals for a single bar.
    /// </summary>
    /// <param name="intervals">The stored intervals, ordered by start.</param>
    /// <param name="barStart">The inclusive local bar start.</param>
    /// <param name="barEnd">The exclusive local bar end.</param>
    /// <returns>The bar value and the total covered ticks (zero when no data overlaps the bar).</returns>
    private static (decimal Value, long TotalTicks) ComputeBar(
        List<(DateTime Start, DateTime End, decimal Price)> intervals,
        DateTime barStart,
        DateTime barEnd)
    {
        decimal weighted = 0m;
        long totalTicks = 0;

        foreach (var interval in intervals)
        {
            if (interval.End <= barStart)
            {
                continue;
            }

            if (interval.Start >= barEnd)
            {
                break;
            }

            var overlapStart = interval.Start > barStart ? interval.Start : barStart;
            var overlapEnd = interval.End < barEnd ? interval.End : barEnd;
            var ticks = (overlapEnd - overlapStart).Ticks;

            if (ticks <= 0)
            {
                continue;
            }

            weighted += interval.Price * ticks;
            totalTicks += ticks;
        }

        return totalTicks > 0 ? (weighted / totalTicks, totalTicks) : (0m, 0);
    }

    /// <summary>
    /// Cache key for a computed aggregate: a specific bar (by local start) at a specific resolution for a domain.
    /// </summary>
    /// <param name="Domain">The price domain.</param>
    /// <param name="Unit">The resolution unit.</param>
    /// <param name="Count">The resolution count.</param>
    /// <param name="BarStartLocal">The local bar start.</param>
    private readonly record struct AggregateKey(string Domain, CalendarUnit Unit, int Count, DateTime BarStartLocal);
}
