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

        var slots = await _repository.GetRangeAsync(
            domain,
            fromLocal.ToUniversalTime(),
            toLocal.ToUniversalTime(),
            cancellationToken);

        return new PriceView
        {
            Bars = Resample(slots, fromLocal, toLocal, level.Resolution),
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
    /// Resample stored slots onto a fixed-resolution grid, time-weighting the average where a bar spans several
    /// slots and repeating a value where a bar is finer than the slot covering it.
    /// </summary>
    /// <param name="slots">The stored slots overlapping the window.</param>
    /// <param name="fromLocal">The inclusive local window start.</param>
    /// <param name="toLocal">The exclusive local window end.</param>
    /// <param name="resolution">The width of one bar.</param>
    /// <returns>The bars, keyed by local start, omitting any bar with no data.</returns>
    private static SortedList<DateTime, decimal> Resample(
        IReadOnlyCollection<PricePoint> slots,
        DateTime fromLocal,
        DateTime toLocal,
        CalendarStep resolution)
    {
        var intervals = slots
            .Where(s => s.DateBegin.HasValue && s.DateEnd.HasValue)
            .Select(s => (Start: s.DateBegin!.Value.ToLocalTime(), End: s.DateEnd!.Value.ToLocalTime(), s.Price))
            .OrderBy(i => i.Start)
            .ToList();

        var bars = new SortedList<DateTime, decimal>();

        for (var barStart = fromLocal; barStart < toLocal; barStart = resolution.AddTo(barStart))
        {
            var barEnd = resolution.AddTo(barStart);
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

            if (totalTicks > 0)
            {
                bars[barStart] = weighted / totalTicks;
            }
        }

        return bars;
    }
}
