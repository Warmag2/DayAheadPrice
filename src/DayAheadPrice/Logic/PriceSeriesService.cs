using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Faemiyah.DayAheadPrice.Database.Repositories;
using Faemiyah.DayAheadPrice.Entities;
using Faemiyah.DayAheadPrice.Enums;
using Faemiyah.DayAheadPrice.Extensions;
using Faemiyah.DayAheadPrice.Options;
using Microsoft.Extensions.Options;

namespace Faemiyah.DayAheadPrice.Logic;

/// <summary>
/// Reads stored price slots and resamples them to the resolution of a requested zoom window.
/// </summary>
internal class PriceSeriesService
{
    private const int LiveHoursBefore = 12;

    // The least data a local day must hold to be summarised, so a thin sliver left by the caller's timezone differing
    // from the market's (e.g. one hour spilling past local midnight) is skipped in favour of the real last day.
    private const int MinimumDaySummaryCoverageHours = 12;

    // How many days back from the newest stored day the summary looks for one with enough coverage. Each day costs a
    // query, and the summary endpoint is anonymous, so the walk is bounded even when recent days are unavailable.
    private const int MaxDaySummaryCandidates = 7;

    // Times of day in the summary (peak slot, alert hour), on the display zone's clock.
    private const string TimeOfDayFormat = "HH\\:mm";

    // How far past now an anchor may lie. Day-ahead prices never reach further ahead than this, so a later anchor
    // shows the same (latest) window.
    private static readonly TimeSpan MaxAnchorLead = TimeSpan.FromDays(2);

    private readonly PricePointRepository _repository;
    private readonly PriceIngestionService _ingestion;
    private readonly LivePriceState _liveState;
    private readonly EndpointOptions _endpointOptions;
    private readonly PricingOptions _pricingOptions;
    private readonly TimeZoneInfo _zone;
    private readonly TimeProvider _timeProvider;

    // Raw (pre-margin/VAT) time-weighted averages for fully-covered, wholly-past bars, keyed by domain + resolution
    // + UTC bar start. Such periods are immutable, so re-reads of coarse windows avoid rescanning thousands of raw
    // rows. Cleared implicitly only by process restart.
    private readonly ConcurrentDictionary<AggregateKey, decimal?> _aggregateCache = new();

    // The last day summary and the data version it was built from; see GetLatestDaySummaryAsync.
    private CachedSummary? _summaryCache;

    /// <summary>
    /// Initializes a new instance of the <see cref="PriceSeriesService"/> class.
    /// </summary>
    /// <param name="repository">The price repository.</param>
    /// <param name="ingestion">The ingestion service, used to backfill missing history from the API.</param>
    /// <param name="liveState">The shared live-view cache and freshness authority.</param>
    /// <param name="endpointOptions">The endpoint options, supplying the domain to read.</param>
    /// <param name="pricingOptions">The pricing options, supplying the default margin and VAT.</param>
    /// <param name="zone">The display time zone, whose calendar bars and days follow.</param>
    /// <param name="timeProvider">The source of the current time.</param>
    public PriceSeriesService(
        PricePointRepository repository,
        PriceIngestionService ingestion,
        LivePriceState liveState,
        IOptions<EndpointOptions> endpointOptions,
        IOptions<PricingOptions> pricingOptions,
        TimeZoneInfo zone,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _ingestion = ingestion;
        _liveState = liveState;
        _endpointOptions = endpointOptions.Value;
        _pricingOptions = pricingOptions.Value;
        _zone = zone;
        _timeProvider = timeProvider;
    }

    private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

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

        // The live view is served from an in-memory snapshot, so the most-requested page touches the database only
        // when the snapshot is absent (at startup or after a write).
        if (state.IsLive)
        {
            return await RenderLiveAsync(domain, level, state.ZoomIndex, cancellationToken);
        }

        // For a positioned (non-live) view, make sure the history it wants is stored, fetching it from the API when
        // the user has panned or zoomed back past what we hold. A little extra is fetched beyond the window edge so
        // that panning further left keeps revealing older data rather than stopping at the first gap.
        var nowUtc = UtcNow;
        var fromUtc = ClampAnchor(state.AnchorUtc ?? nowUtc, nowUtc).FloorLocal(level.AlignUnit, _zone);

        var bounds = await _ingestion.EnsureRangeAsync(level.Span.SubtractFrom(fromUtc, _zone), level.Span.AddTo(fromUtc, _zone), cancellationToken);

        if (bounds == null)
        {
            return EmptyView(level, state.ZoomIndex, false);
        }

        var (minUtc, maxUtc) = bounds.Value;

        var earliestStart = minUtc.FloorLocal(level.AlignUnit, _zone);
        var latestStart = maxUtc.FloorLocal(level.AlignUnit, _zone);

        if (fromUtc < earliestStart)
        {
            fromUtc = earliestStart;
        }
        else if (fromUtc > latestStart)
        {
            fromUtc = latestStart;
        }

        var toUtc = level.Span.AddTo(fromUtc, _zone);

        var bars = await ResampleWindowAsync(domain, level.Resolution, fromUtc, toUtc, cancellationToken);

        return new PriceView
        {
            Bars = bars,
            Resolution = level.Resolution,
            HeaderUnit = level.HeaderUnit,
            FromUtc = fromUtc,
            ToUtc = toUtc,
            ZoomIndex = state.ZoomIndex,
            IsLive = false,
            CanPanLeft = fromUtc > minUtc,
            CanPanRight = toUtc < maxUtc
        };
    }

    /// <summary>
    /// Bring a (user-supplied) anchor into the range prices can exist in, so calendar arithmetic on it cannot overflow
    /// and a far-off anchor costs nothing more than one at the edge of the data.
    /// </summary>
    /// <param name="anchorUtc">The requested anchor, UTC.</param>
    /// <param name="nowUtc">The current time, UTC.</param>
    /// <returns>The anchor, clamped to [<see cref="Market.HistoryStartUtc"/>, now + <see cref="MaxAnchorLead"/>].</returns>
    private static DateTime ClampAnchor(DateTime anchorUtc, DateTime nowUtc)
    {
        var latestUtc = nowUtc + MaxAnchorLead;

        if (anchorUtc < Market.HistoryStartUtc)
        {
            return Market.HistoryStartUtc;
        }

        return anchorUtc > latestUtc ? latestUtc : anchorUtc;
    }

    /// <summary>
    /// Summarise the most recent sufficiently-covered day: its lowest, highest and time-weighted average price.
    /// </summary>
    /// <remarks>
    /// Fetches and persists newer data first, exactly as a live page render would, so an access that is the first of
    /// the day still returns the freshly published prices. Prices come from ENTSO-e aligned to the market's day, but
    /// the app works in the configured display time zone (<see cref="DisplayOptions.TimeZone"/>); when the two differ,
    /// the newest local day holds only a thin
    /// sliver of data (an hour or two spilling past local midnight). To avoid reporting such a sliver, the newest
    /// local day holding at least <see cref="MinimumDaySummaryCoverageHours"/> hours of data is summarised instead,
    /// looking at most <see cref="MaxDaySummaryCandidates"/> days back. The summary depends only on stored data, so it
    /// is cached and rebuilt only after a write (a <see cref="LivePriceState.Version"/> change); the endpoint serving it
    /// is anonymous, and a rebuild costs several queries.
    /// </remarks>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The summary, or <b>null</b> when no recent day has enough stored data.</returns>
    public async Task<PriceDaySummary?> GetLatestDaySummaryAsync(CancellationToken cancellationToken = default)
    {
        // Pull and persist newer data when the stored window no longer reaches far enough ahead, just as the live view does.
        await _ingestion.EnsureFreshAsync(cancellationToken);

        // Read the version before building, so a write during the build leaves the cache stale rather than wrong.
        var version = _liveState.Version;

        if (_summaryCache is { } cached && cached.Version == version)
        {
            return cached.Summary;
        }

        var summary = await BuildLatestDaySummaryAsync(_endpointOptions.Domain, cancellationToken);

        _summaryCache = new CachedSummary(version, summary);

        return summary;
    }

    /// <summary>
    /// Build the summary of the most recent sufficiently-covered day from the database.
    /// </summary>
    /// <param name="domain">The price domain.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The summary, or <b>null</b> when no recent day has enough stored data.</returns>
    private async Task<PriceDaySummary?> BuildLatestDaySummaryAsync(string domain, CancellationToken cancellationToken)
    {
        var bounds = await _repository.GetBoundsAsync(domain, cancellationToken);

        if (bounds == null)
        {
            return null;
        }

        var earliestDay = DateOnly.FromDateTime(bounds.Value.MinUtc.ToLocalWall(_zone));

        // The latest stored slot ends (exclusive) at this instant, so its final tick is in the newest covered day.
        var candidate = DateOnly.FromDateTime(bounds.Value.MaxUtc.AddTicks(-1).ToLocalWall(_zone));

        // Walk back from the newest day to the newest one with enough coverage, skipping a short sliver left by the
        // caller's timezone differing from the market's. Bounded, since every candidate costs a query.
        for (var i = 0; i < MaxDaySummaryCandidates && candidate >= earliestDay; i++)
        {
            var summary = await TryBuildDaySummaryAsync(domain, candidate, cancellationToken);

            if (summary != null)
            {
                return summary;
            }

            candidate = candidate.AddDays(-1);
        }

        return null;
    }

    /// <summary>
    /// Build a one-day summary for a specific local day, or <b>null</b> when that day holds fewer than
    /// <see cref="MinimumDaySummaryCoverageHours"/> hours of data.
    /// </summary>
    /// <param name="domain">The price domain.</param>
    /// <param name="day">The local calendar day to summarise.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The summary, or <b>null</b> when the day lacks enough data.</returns>
    private async Task<PriceDaySummary?> TryBuildDaySummaryAsync(string domain, DateOnly day, CancellationToken cancellationToken)
    {
        // A local day is 23 or 25 hours long across a daylight-saving change, so resolve both of its ends to UTC.
        var dayStartUtc = day.ToDateTime(TimeOnly.MinValue).LocalToUtc(_zone);
        var dayEndUtc = day.AddDays(1).ToDateTime(TimeOnly.MinValue).LocalToUtc(_zone);

        var slots = await _repository.GetRangeAsync(domain, dayStartUtc, dayEndUtc, cancellationToken);

        var intervals = slots.ToIntervals();

        var (average, _, pricedTicks) = PriceResampler.ComputeBar(intervals, dayStartUtc, dayEndUtc);

        // Require enough of the day to be priced so a thin timezone-induced sliver (or a mostly unavailable day) is
        // not reported as "the last day".
        if (average is not { } dayAverage || pricedTicks < TimeSpan.FromHours(MinimumDaySummaryCoverageHours).Ticks)
        {
            return null;
        }

        var priced = intervals
            .Where(i => i.Price.HasValue)
            .Select(i => (i.StartUtc, Price: i.Price!.Value))
            .ToList();

        var currency = slots.Select(s => s.Currency).FirstOrDefault() ?? Market.Currency;

        var margin = _pricingOptions.Margin;
        var vat = _pricingOptions.Vat;

        // The slot carrying the day's highest price; ties resolve to the earliest, since intervals are ordered by start.
        var (peakTime, peakPrice) = priced.MaxBy(i => i.Price);

        // Alert thresholds are in final consumer prices, so compare against values with margin and VAT applied.
        var finalAverage = dayAverage.ApplyMarginAndVat(margin, vat);
        var finalPeak = peakPrice.ApplyMarginAndVat(margin, vat);

        var alerts = new List<string>();

        // Calculate any alertworthy conditions for the day, from the most important to least important
        if (finalAverage > _pricingOptions.AlertAverageThreshold)
        {
            alerts.Add("High average price.");
        }

        // The hour with the highest time-weighted average; ties resolve to the earliest hour. Hours are stepped on the
        // local clock, so a repeated hour (clocks going back) is considered twice and a skipped one not at all.
        var (peakHourStartUtc, peakHourPrice) = PriceResampler
            .ResampleIntervals(intervals, dayStartUtc, dayEndUtc, new CalendarStep(CalendarUnit.Hour, 1), _zone)
            .Where(b => b.Value.HasValue)
            .Select(b => (StartUtc: b.Key, Price: b.Value!.Value))
            .MaxBy(b => b.Price);

        if (peakHourPrice.ApplyMarginAndVat(margin, vat) > _pricingOptions.AlertHourThreshold)
        {
            alerts.Add($"High average price during hour {peakHourStartUtc.ToLocalWall(_zone).ToString(TimeOfDayFormat, CultureInfo.InvariantCulture)}.");
        }

        if (finalPeak > _pricingOptions.AlertMaxThreshold)
        {
            alerts.Add("High peak price.");
        }

        return new PriceDaySummary(
            domain,
            day,
            currency,
            Math.Round(priced.Min(i => i.Price).ApplyMarginAndVat(margin, vat), 2, MidpointRounding.AwayFromZero),
            Math.Round(finalPeak, 2, MidpointRounding.AwayFromZero),
            TimeOnly.FromDateTime(peakTime.ToLocalWall(_zone)).ToString(TimeOfDayFormat, CultureInfo.InvariantCulture),
            Math.Round(finalAverage, 2, MidpointRounding.AwayFromZero),
            margin,
            vat,
            alerts);
    }

    /// <summary>
    /// Render the live (current) window from the in-memory snapshot, loading it from the database only when there is
    /// none (at startup, or after a write dropped it).
    /// </summary>
    /// <remarks>
    /// The snapshot is served whether or not it is fresh: a stale snapshot still holds everything stored, because
    /// every write drops it. Freshness only decides whether to ask the API for new data.
    /// </remarks>
    /// <param name="domain">The price domain.</param>
    /// <param name="level">The (finest) zoom level.</param>
    /// <param name="zoomIndex">The zoom index.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The rendered live view.</returns>
    private async Task<PriceView> RenderLiveAsync(string domain, ZoomLevel level, int zoomIndex, CancellationToken cancellationToken)
    {
        // Read the snapshot once, so a concurrent invalidation causes a reload rather than an empty view.
        var snapshot = _liveState.Current ?? await RefreshLiveSnapshotAsync(domain, cancellationToken);

        if (snapshot == null)
        {
            return EmptyView(level, zoomIndex, true);
        }

        var (fromUtc, toUtc) = LiveWindow(snapshot.MaxUtc);

        return new PriceView
        {
            Bars = PriceResampler.ResampleIntervals(snapshot.Intervals, fromUtc, toUtc, level.Resolution, _zone),
            Resolution = level.Resolution,
            HeaderUnit = level.HeaderUnit,
            FromUtc = fromUtc,
            ToUtc = toUtc,
            ZoomIndex = zoomIndex,
            IsLive = true,
            CanPanLeft = fromUtc > snapshot.MinUtc,
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
        // Taken before reading, so a write landing mid-load discards this (possibly incomplete) snapshot.
        var version = _liveState.Version;

        var bounds = await _repository.GetBoundsAsync(domain, cancellationToken);

        if (bounds == null)
        {
            return null;
        }

        var (minUtc, maxUtc) = bounds.Value;

        var (fromUtc, toUtc) = LiveWindow(maxUtc);

        var slots = await _repository.GetRangeAsync(domain, fromUtc, toUtc, cancellationToken);

        var intervals = slots.ToIntervals();

        var snapshot = new LiveSnapshot(minUtc, maxUtc, intervals);
        _liveState.Set(snapshot, version);

        return snapshot;
    }

    /// <summary>
    /// The live window: from <see cref="LiveHoursBefore"/> hours before the current hour to the end of stored data,
    /// or a day ahead when stored data ends earlier.
    /// </summary>
    /// <param name="maxUtc">The end of stored data, UTC.</param>
    /// <returns>The window, UTC.</returns>
    private (DateTime FromUtc, DateTime ToUtc) LiveWindow(DateTime maxUtc)
    {
        var fromUtc = UtcNow.FloorLocal(CalendarUnit.Hour, _zone).AddHours(-LiveHoursBefore);

        return (fromUtc, maxUtc > fromUtc ? maxUtc : fromUtc.AddDays(1));
    }

    /// <summary>
    /// An empty view anchored at the current quarter, used when nothing is stored for the domain.
    /// </summary>
    /// <param name="level">The zoom level.</param>
    /// <param name="zoomIndex">The zoom index.</param>
    /// <param name="isLive">Whether the view is the live view.</param>
    /// <returns>The empty view.</returns>
    private PriceView EmptyView(ZoomLevel level, int zoomIndex, bool isLive)
    {
        var nowQuarterUtc = UtcNow.FloorLocal(CalendarUnit.Quarter, _zone);

        return new PriceView
        {
            Resolution = level.Resolution,
            HeaderUnit = level.HeaderUnit,
            FromUtc = nowQuarterUtc,
            ToUtc = nowQuarterUtc,
            ZoomIndex = zoomIndex,
            IsLive = isLive
        };
    }

    /// <summary>
    /// Produce the bars for a window, serving fully-covered past bars from the in-memory aggregate cache and reading
    /// the database only for the span of bars still uncached. Any real holes inside the read span are filled from the
    /// API before resampling.
    /// </summary>
    /// <param name="domain">The price domain.</param>
    /// <param name="resolution">The width of one bar.</param>
    /// <param name="fromUtc">The inclusive window start, UTC.</param>
    /// <param name="toUtc">The exclusive window end, UTC.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The bars, keyed by UTC start, omitting any bar with no data.</returns>
    private async Task<SortedList<DateTime, decimal?>> ResampleWindowAsync(
        string domain,
        CalendarStep resolution,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        var bars = new SortedList<DateTime, decimal?>();
        var uncached = new List<DateTime>();

        foreach (var barStart in PriceResampler.EnumerateBarStarts(fromUtc, toUtc, resolution, _zone))
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
        var readFromUtc = uncached[0];
        var readToUtc = resolution.AddTo(uncached[^1], _zone);

        var slots = await _repository.GetRangeAsync(domain, readFromUtc, readToUtc, cancellationToken);

        // Fill any real holes inside the read span (e.g. a period nobody looked at) from the API, then re-read so the
        // freshly fetched slots are rendered. FillGapsAsync only fetches past ranges and throttles repeats, so a
        // healthy span (no gaps) costs nothing here.
        var gaps = PriceResampler.ComputeGaps(slots, readFromUtc, readToUtc);

        if (gaps.Count > 0 && await _ingestion.FillGapsAsync(gaps, cancellationToken))
        {
            slots = await _repository.GetRangeAsync(domain, readFromUtc, readToUtc, cancellationToken);
        }

        var intervals = slots.ToIntervals();

        var nowUtc = UtcNow;

        foreach (var barStart in uncached)
        {
            var barEnd = resolution.AddTo(barStart, _zone);
            var (value, coveredTicks, _) = PriceResampler.ComputeBar(intervals, barStart, barEnd);

            if (coveredTicks <= 0)
            {
                continue;
            }

            bars[barStart] = value;

            // Cache only a fully-covered bar that lies wholly in the past; such a period can no longer change, so a
            // later re-read need not rescan its raw rows. A partially-covered or still-open bar is left uncached.
            var fullyCovered = coveredTicks == (barEnd - barStart).Ticks;

            if (fullyCovered && barEnd <= nowUtc)
            {
                _aggregateCache[new AggregateKey(domain, resolution.Unit, resolution.Count, barStart)] = value;
            }
        }

        return bars;
    }

    /// <summary>
    /// Cache key for a computed aggregate: a specific bar (by UTC start) at a specific resolution for a domain.
    /// </summary>
    /// <param name="Domain">The price domain.</param>
    /// <param name="Unit">The resolution unit.</param>
    /// <param name="Count">The resolution count.</param>
    /// <param name="BarStartUtc">The bar start, UTC.</param>
    private readonly record struct AggregateKey(string Domain, CalendarUnit Unit, int Count, DateTime BarStartUtc);

    /// <summary>
    /// A cached day summary.
    /// </summary>
    /// <param name="Version">The <see cref="LivePriceState.Version"/> the summary was built from.</param>
    /// <param name="Summary">The summary; <b>null</b> when no recent day had enough stored data.</param>
    private sealed record CachedSummary(long Version, PriceDaySummary? Summary);
}
