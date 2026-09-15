using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DayAheadPrice.Database.Entities;
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
    private readonly LivePriceState _liveState;
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
    /// <param name="liveState">The shared live-view cache and freshness authority.</param>
    /// <param name="endpointOptions">The endpoint options, supplying the domain to read.</param>
    public PriceSeriesService(
        PricePointRepository repository,
        PriceContainer container,
        LivePriceState liveState,
        IOptions<EndpointOptions> endpointOptions)
    {
        _repository = repository;
        _container = container;
        _liveState = liveState;
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

        // The live view is served from an in-memory snapshot so the most-requested page never touches the database
        // while the cached data is still fresh (reaches far enough into the future).
        if (isLive)
        {
            return await RenderLiveAsync(domain, level, state.ZoomIndex, cancellationToken);
        }

        // For a positioned (non-live) view, make sure the history it wants is stored, fetching it from the API when
        // the user has panned or zoomed back past what we hold. A little extra is fetched beyond the window edge so
        // that panning further left keeps revealing older data rather than stopping at the first gap.
        var wantFrom = (state.AnchorLocal ?? DateTime.Now).Floor(level.AlignUnit);
        var wantTo = level.Span.AddTo(wantFrom);

        await _container.EnsureRangeAsync(
            level.Span.SubtractFrom(wantFrom).ToUniversalTime(),
            wantTo.ToUniversalTime(),
            cancellationToken);

        var bounds = await _repository.GetBoundsAsync(domain, cancellationToken);

        if (bounds == null)
        {
            return EmptyView(level, state.ZoomIndex, false);
        }

        var minLocal = bounds.Value.MinUtc.ToLocalTime();
        var maxLocal = bounds.Value.MaxUtc.ToLocalTime();

        var anchor = state.AnchorLocal ?? DateTime.Now;
        var fromLocal = anchor.Floor(level.AlignUnit);

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

        var toLocal = level.Span.AddTo(fromLocal);

        var bars = await ResampleWindowAsync(domain, level.Resolution, fromLocal, toLocal, cancellationToken);

        return new PriceView
        {
            Bars = bars,
            Resolution = level.Resolution,
            HeaderUnit = level.HeaderUnit,
            FromLocal = fromLocal,
            ToLocal = toLocal,
            ZoomIndex = state.ZoomIndex,
            IsLive = false,
            CanPanLeft = fromLocal > minLocal,
            CanPanRight = toLocal < maxLocal
        };
    }

    /// <summary>
    /// Render the live (current) window, serving from the in-memory snapshot while it is fresh and otherwise
    /// reloading it once from the database.
    /// </summary>
    /// <param name="domain">The price domain.</param>
    /// <param name="level">The (finest) zoom level.</param>
    /// <param name="zoomIndex">The zoom index.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The rendered live view.</returns>
    private async Task<PriceView> RenderLiveAsync(string domain, ZoomLevel level, int zoomIndex, CancellationToken cancellationToken)
    {
        var snapshot = _liveState.IsFresh ? _liveState.Current : await RefreshLiveSnapshotAsync(domain, cancellationToken);

        if (snapshot == null)
        {
            return EmptyView(level, zoomIndex, true);
        }

        var fromLocal = DateTime.Now.Floor(CalendarUnit.Hour).AddHours(-LiveHoursBefore);
        var toLocal = snapshot.MaxLocal > fromLocal ? snapshot.MaxLocal : fromLocal.AddDays(1);

        return new PriceView
        {
            Bars = ResampleIntervals(snapshot.Intervals, fromLocal, toLocal, level.Resolution),
            Resolution = level.Resolution,
            HeaderUnit = level.HeaderUnit,
            FromLocal = fromLocal,
            ToLocal = toLocal,
            ZoomIndex = zoomIndex,
            IsLive = true,
            CanPanLeft = fromLocal > snapshot.MinLocal,
            CanPanRight = false
        };
    }

    /// <summary>
    /// Reload the live snapshot from the database (bounds plus the slots covering the live window) and cache it.
    /// </summary>
    /// <param name="domain">The price domain.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The new snapshot, or <b>null</b> when nothing is stored for the domain.</returns>
    private async Task<LiveSnapshot?> RefreshLiveSnapshotAsync(string domain, CancellationToken cancellationToken)
    {
        var bounds = await _repository.GetBoundsAsync(domain, cancellationToken);

        if (bounds == null)
        {
            _liveState.Invalidate();

            return null;
        }

        var minLocal = bounds.Value.MinUtc.ToLocalTime();
        var maxLocal = bounds.Value.MaxUtc.ToLocalTime();

        var fromLocal = DateTime.Now.Floor(CalendarUnit.Hour).AddHours(-LiveHoursBefore);
        var toLocal = maxLocal > fromLocal ? maxLocal : fromLocal.AddDays(1);

        var slots = await _repository.GetRangeAsync(
            domain,
            fromLocal.ToUniversalTime(),
            toLocal.ToUniversalTime(),
            cancellationToken);

        var intervals = slots
            .Where(s => s.DateBegin.HasValue && s.DateEnd.HasValue)
            .Select(s => (StartLocal: s.DateBegin!.Value.ToLocalTime(), EndLocal: s.DateEnd!.Value.ToLocalTime(), s.Price))
            .OrderBy(i => i.StartLocal)
            .ToList();

        var snapshot = new LiveSnapshot(minLocal, maxLocal, intervals);
        _liveState.Set(snapshot);

        return snapshot;
    }

    /// <summary>
    /// An empty view anchored at the current quarter, used when nothing is stored for the domain.
    /// </summary>
    /// <param name="level">The zoom level.</param>
    /// <param name="zoomIndex">The zoom index.</param>
    /// <param name="isLive">Whether the view is the live view.</param>
    /// <returns>The empty view.</returns>
    private static PriceView EmptyView(ZoomLevel level, int zoomIndex, bool isLive)
    {
        return new PriceView
        {
            Resolution = level.Resolution,
            HeaderUnit = level.HeaderUnit,
            FromLocal = DateTime.Now.Floor(CalendarUnit.Quarter),
            ToLocal = DateTime.Now.Floor(CalendarUnit.Quarter),
            ZoomIndex = zoomIndex,
            IsLive = isLive
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
    /// Resample a set of local-time intervals onto a fixed-resolution grid, without any database access or caching.
    /// Used for the live view, whose intervals come from the in-memory snapshot.
    /// </summary>
    /// <param name="intervals">The intervals, ordered by start, in local time.</param>
    /// <param name="fromLocal">The inclusive local window start.</param>
    /// <param name="toLocal">The exclusive local window end.</param>
    /// <param name="resolution">The width of one bar.</param>
    /// <returns>The bars, keyed by local start, omitting any bar with no data.</returns>
    private static SortedList<DateTime, decimal> ResampleIntervals(
        IReadOnlyList<(DateTime StartLocal, DateTime EndLocal, decimal Price)> intervals,
        DateTime fromLocal,
        DateTime toLocal,
        CalendarStep resolution)
    {
        var bars = new SortedList<DateTime, decimal>();

        for (var barStart = fromLocal; barStart < toLocal; barStart = resolution.AddTo(barStart))
        {
            var barEnd = resolution.AddTo(barStart);
            var (value, totalTicks) = ComputeBar(intervals, barStart, barEnd);

            if (totalTicks > 0)
            {
                bars[barStart] = value;
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
        IReadOnlyList<(DateTime Start, DateTime End, decimal Price)> intervals,
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
