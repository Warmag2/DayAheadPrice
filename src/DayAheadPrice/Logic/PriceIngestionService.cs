using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Faemiyah.DayAheadPrice.Database.Entities;
using Faemiyah.DayAheadPrice.Database.Repositories;
using Faemiyah.DayAheadPrice.Entities;
using Faemiyah.DayAheadPrice.Enums;
using Faemiyah.DayAheadPrice.Extensions;
using Faemiyah.DayAheadPrice.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Faemiyah.DayAheadPrice.Logic;

/// <summary>
/// Brings published prices into the database: keeps them fresh, backfills history and fills gaps.
/// </summary>
internal class PriceIngestionService
{
    private static readonly TimeSpan GapRetryCooldown = TimeSpan.FromHours(6);

    // How long after a slot ends before a missing price is taken as final and stored as unavailable. Day-ahead
    // prices are published before delivery, so a recently ended slot is given a day for any late publication.
    private static readonly TimeSpan UnavailableSettleTime = TimeSpan.FromDays(1);

    private readonly EndpointOptions _endpointOptions;
    private readonly IPriceSource _source;
    private readonly PricePointRepository _repository;
    private readonly LivePriceState _liveState;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PriceIngestionService> _logger;
    private readonly List<GapAttempt> _gapAttempts = [];
    private readonly Lock _gapLock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="PriceIngestionService"/> class.
    /// </summary>
    /// <param name="logger">The logging endpoint.</param>
    /// <param name="endpointOptions">The endpoint options.</param>
    /// <param name="source">The source of published prices.</param>
    /// <param name="repository">The price repository.</param>
    /// <param name="liveState">The shared live-view cache and freshness authority.</param>
    /// <param name="timeProvider">The source of the current time.</param>
    public PriceIngestionService(
        ILogger<PriceIngestionService> logger,
        IOptions<EndpointOptions> endpointOptions,
        IPriceSource source,
        PricePointRepository repository,
        LivePriceState liveState,
        TimeProvider timeProvider)
    {
        _endpointOptions = endpointOptions.Value;
        _source = source;
        _repository = repository;
        _liveState = liveState;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

    /// <summary>
    /// A record of one gap-fill attempt, used to avoid repeatedly requesting a range the API failed to serve and to
    /// reserve an in-flight range so concurrent callers do not request it at the same time.
    /// </summary>
    private sealed class GapAttempt
    {
        public GapAttempt(DateTime fromUtc, DateTime toUtc, DateTime attemptedAtUtc)
        {
            FromUtc = fromUtc;
            ToUtc = toUtc;
            AttemptedAtUtc = attemptedAtUtc;
        }

        /// <summary>Gets the attempted range start, UTC.</summary>
        public DateTime FromUtc { get; }

        /// <summary>Gets the attempted range end, UTC.</summary>
        public DateTime ToUtc { get; }

        /// <summary>Gets or sets when the attempt was made, UTC.</summary>
        public DateTime AttemptedAtUtc { get; set; }
    }

    /// <summary>
    /// Ensure the database holds current prices, fetching and persisting from the API only when the stored data
    /// no longer extends far enough into the future.
    /// </summary>
    /// <remarks>
    /// The lookahead rule: while stored prices reach more than <see cref="LivePriceState.LookaheadHours"/> hours
    /// ahead, no request is made, so on a normal day the first request happens at 13:00 CET/CEST (14:00 in Finland),
    /// shortly after the market publishes tomorrow's prices at about 12:45 CET/CEST. The same constant drives
    /// <see cref="LivePriceState.IsFresh"/>, so the hourly throttle slot is never spent before data can be stale.
    /// </remarks>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task EnsureFreshAsync(CancellationToken cancellationToken = default)
    {
        // The cached live data still reaches far enough into the future; nothing to do and no database access needed.
        if (_liveState.IsFresh)
        {
            return;
        }

        // Throttle to at most one check per hour, so a failing or not-yet-updated API is not hammered, and so a stale
        // period costs one bounds query per hour rather than one per page view.
        if (!_liveState.TryBeginRefreshCheck())
        {
            return;
        }

        var bounds = await _repository.GetBoundsAsync(_endpointOptions.Domain, cancellationToken);

        // Stored data still covers enough of the future; nothing new is likely to be published yet.
        if (bounds != null && bounds.Value.MaxUtc > UtcNow.AddHours(LivePriceState.LookaheadHours))
        {
            return;
        }

        try
        {
            _logger.LogInformation("Stored prices are stale. Fetching new data from the API.");

            // Fetch forward from the last stored slot (not a fixed window) so a long idle period cannot leave a
            // hole between the old data and the freshly fetched block.
            var nowUtc = UtcNow;
            var startUtc = bounds != null ? bounds.Value.MaxUtc : nowUtc.AddDays(-1);

            var priceList = await FetchAsync(startUtc, nowUtc.AddDays(1), cancellationToken);

            if (priceList.Prices.Count == 0)
            {
                _logger.LogWarning("The API has no prices from {From} onwards. They may not be published yet, or the configured domain may be wrong.", startUtc);

                return;
            }

            await PersistAsync(priceList.Prices.Select(p => (p.Key, (decimal?)p.Value)).ToList(), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller went away; that says nothing about the API, so let the next caller check this hour again.
            _liveState.ReleaseRefreshCheck();

            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to fetch and persist price information.");
        }
    }

    /// <summary>
    /// Ensure stored prices cover the history in [<paramref name="fromUtc"/>, <paramref name="toUtc"/>) that lies
    /// before the earliest stored slot, fetching and persisting it from the API.
    /// </summary>
    /// <remarks>
    /// A no-op when the requested start is already covered by stored data. The fetch goes through
    /// <see cref="FillGapsAsync"/>, so it is chunked, throttled and safe under concurrent callers. It stops at
    /// <paramref name="toUtc"/> even when stored data starts later, so a far-past anchor costs only its own window;
    /// any hole left between the window and the stored data is filled when a view actually reads it.
    /// </remarks>
    /// <param name="fromUtc">The desired earliest slot start, UTC.</param>
    /// <param name="toUtc">The exclusive end of the requested range, UTC.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The stored bounds afterwards, as <see cref="PricePointRepository.GetBoundsAsync"/> gives them.</returns>
    public async Task<(DateTime MinUtc, DateTime MaxUtc)?> EnsureRangeAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
    {
        var bounds = await _repository.GetBoundsAsync(_endpointOptions.Domain, cancellationToken);

        // Stored data already reaches back at least this far.
        if (bounds != null && bounds.Value.MinUtc <= fromUtc)
        {
            return bounds;
        }

        var endUtc = bounds != null && bounds.Value.MinUtc < toUtc ? bounds.Value.MinUtc : toUtc;

        return await FillGapsAsync([(fromUtc, endUtc)], cancellationToken)
            ? await _repository.GetBoundsAsync(_endpointOptions.Domain, cancellationToken)
            : bounds;
    }

    /// <summary>
    /// Fill the given missing time ranges (UTC) by fetching them from the API and persisting the result.
    /// </summary>
    /// <remarks>
    /// Only ranges wholly in the past and no earlier than <see cref="Market.HistoryStartUtc"/> are fetched here; near-future
    /// and current data is the freshness path's responsibility. Each range is split at UTC month boundaries, so a
    /// single request never asks the API for more than a month, and overlapping ranges from different callers
    /// (different anchors or zoom levels) produce the same chunks and are recognised as already attempted. Every chunk
    /// is reserved before it is requested, so concurrent callers (e.g. several users panning far back at once) never
    /// issue the same request twice; a reserved or already-attempted chunk is skipped until the retry cooldown
    /// elapses. Settled slots the API has no price for are stored as unavailable, so they are never requested again.
    /// </remarks>
    /// <param name="gapsUtc">The missing ranges, each an inclusive start / exclusive end in UTC.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><b>True</b> when at least one range yielded new slots (priced or unavailable) that were persisted.</returns>
    public async Task<bool> FillGapsAsync(
        IReadOnlyList<(DateTime FromUtc, DateTime ToUtc)> gapsUtc,
        CancellationToken cancellationToken = default)
    {
        var nowUtc = UtcNow;
        var changed = false;

        foreach (var (gapFromUtc, gapToUtc) in gapsUtc)
        {
            var gapStartUtc = gapFromUtc > Market.HistoryStartUtc ? gapFromUtc : Market.HistoryStartUtc;

            // The future is filled by the freshness path once the API publishes it; only backfill history here.
            var gapEndUtc = gapToUtc < nowUtc ? gapToUtc : nowUtc;

            for (var chunkFromUtc = gapStartUtc; chunkFromUtc < gapEndUtc;)
            {
                var chunkToUtc = new DateTime(chunkFromUtc.Year, chunkFromUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);

                if (chunkToUtc > gapEndUtc)
                {
                    chunkToUtc = gapEndUtc;
                }

                // Reserve the chunk under the lock before awaiting, so a concurrent caller sees it as in-flight and
                // does not issue the same request.
                var reservation = TryReserveGap(chunkFromUtc, chunkToUtc, nowUtc);

                if (reservation != null && await FetchReservedChunkAsync(reservation, cancellationToken))
                {
                    changed = true;
                }

                chunkFromUtc = chunkToUtc;
            }
        }

        return changed;
    }

    /// <summary>
    /// Reserve a chunk for fetching, or return <b>null</b> when it should be skipped because a matching range is
    /// already reserved or was recently attempted.
    /// </summary>
    /// <param name="fromUtc">The chunk start, UTC.</param>
    /// <param name="toUtc">The chunk end, UTC.</param>
    /// <param name="nowUtc">The current instant, UTC.</param>
    /// <returns>The reservation to fetch, or <b>null</b> to skip.</returns>
    private GapAttempt? TryReserveGap(DateTime fromUtc, DateTime toUtc, DateTime nowUtc)
    {
        lock (_gapLock)
        {
            // Drop stale attempts so the list cannot grow without bound.
            _gapAttempts.RemoveAll(a => nowUtc - a.AttemptedAtUtc >= GapRetryCooldown);

            foreach (var attempt in _gapAttempts)
            {
                if (attempt.FromUtc <= fromUtc && attempt.ToUtc >= toUtc)
                {
                    // Reserved or attempted within the cooldown: skip.
                    return null;
                }
            }

            var reservation = new GapAttempt(fromUtc, toUtc, nowUtc);
            _gapAttempts.Add(reservation);

            return reservation;
        }
    }

    /// <summary>
    /// Fetch and persist a reserved chunk, updating the reservation with the outcome.
    /// </summary>
    /// <param name="reservation">The reserved chunk.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><b>True</b> when the chunk yielded new or changed slots (priced or unavailable) that were persisted.</returns>
    private async Task<bool> FetchReservedChunkAsync(GapAttempt reservation, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Filling price gap {From}..{To} for domain {Domain}.", reservation.FromUtc, reservation.ToUtc, _endpointOptions.Domain);

            var priceList = await FetchAsync(reservation.FromUtc, reservation.ToUtc, cancellationToken);

            var attemptedAtUtc = UtcNow;

            lock (_gapLock)
            {
                reservation.AttemptedAtUtc = attemptedAtUtc;
            }

            var slots = WithUnavailableSlots(priceList, reservation.FromUtc, reservation.ToUtc, attemptedAtUtc - UnavailableSettleTime);

            return slots.Count > 0 && await PersistAsync(slots, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller went away; that says nothing about the API, so free the range for the next caller to fetch.
            lock (_gapLock)
            {
                _gapAttempts.Remove(reservation);
            }

            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to fill price gap {From}..{To}.", reservation.FromUtc, reservation.ToUtc);

            // Keep the reservation so the failed range honours the cooldown before it is retried.
            lock (_gapLock)
            {
                reservation.AttemptedAtUtc = UtcNow;
            }

            return false;
        }
    }

    /// <summary>
    /// The slots to store for a fetched range: every fetched price, plus an unavailable (<b>null</b>) slot for each
    /// quarter of the range the API had no price for, once that quarter has settled.
    /// </summary>
    /// <param name="priceList">The fetched prices.</param>
    /// <param name="fromUtc">The inclusive start of the requested range, UTC.</param>
    /// <param name="toUtc">The exclusive end of the requested range, UTC.</param>
    /// <param name="settledBeforeUtc">A missing quarter ending at or before this instant is final.</param>
    /// <returns>The slots, keyed by UTC start, ordered.</returns>
    internal static List<(DateTime SlotUtc, decimal? Price)> WithUnavailableSlots(
        PriceList priceList,
        DateTime fromUtc,
        DateTime toUtc,
        DateTime settledBeforeUtc)
    {
        var slots = new SortedList<DateTime, decimal?>(priceList.Prices.Count);

        foreach (var (slotUtc, price) in priceList.Prices)
        {
            slots[slotUtc] = price;
        }

        for (var slotUtc = fromUtc.Floor(CalendarUnit.Quarter); slotUtc < toUtc && slotUtc + Market.SlotLength <= settledBeforeUtc; slotUtc += Market.SlotLength)
        {
            slots.TryAdd(slotUtc, null);
        }

        return slots.Select(s => (s.Key, s.Value)).ToList();
    }

    /// <summary>
    /// Store the given slots, writing only those that are new or differ from what is already stored.
    /// </summary>
    /// <remarks>
    /// The API answers in whole hours (and in practice whole days), so a fetch often returns slots that are already
    /// stored. The stored range is read once and unchanged slots are left alone; the rest are written together, in
    /// one transaction.
    /// </remarks>
    /// <param name="slots">The slots, keyed by UTC start.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><b>True</b> when any slot was written.</returns>
    /// <exception cref="InvalidOperationException">The write was rejected, for example by a concurrent writer.</exception>
    private async Task<bool> PersistAsync(IReadOnlyCollection<(DateTime SlotUtc, decimal? Price)> slots, CancellationToken cancellationToken)
    {
        var domain = _endpointOptions.Domain;
        var fromUtc = slots.Min(s => s.SlotUtc);
        var toUtc = slots.Max(s => s.SlotUtc) + Market.SlotLength;

        var stored = (await _repository.GetRangeAsync(domain, fromUtc, toUtc, cancellationToken))
            .Where(p => p.DateBegin.HasValue)
            .ToDictionary(p => p.DateBegin!.Value);

        var points = new List<PricePoint>(slots.Count);

        foreach (var (slotUtc, price) in slots)
        {
            var slotEndUtc = slotUtc + Market.SlotLength;

            if (stored.TryGetValue(slotUtc, out var existing)
                && existing.Price == price
                && existing.DateEnd == slotEndUtc
                && existing.Currency == Market.Currency)
            {
                continue;
            }

            points.Add(new PricePoint
            {
                Domain = domain,
                Currency = Market.Currency,
                Price = price,
                DateBegin = slotUtc,
                DateEnd = slotEndUtc
            });
        }

        if (points.Count == 0)
        {
            return false;
        }

        var result = await _repository.AddOrUpdateManyAsync(
            points,
            incoming => p => p.Domain == incoming.Domain && p.DateBegin == incoming.DateBegin,
            cancellationToken);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Storing {points.Count} price slots for domain {domain} failed: {result.Status}.");
        }

        // The live snapshot mirrors the stored data, so every write drops it and the next live render reloads.
        _liveState.Invalidate();

        _logger.LogInformation("Persisted {Count} of {Fetched} fetched price slots for domain {Domain}.", points.Count, slots.Count, domain);

        return true;
    }

    /// <summary>
    /// Fetch the published prices for a range, widened to whole hours.
    /// </summary>
    /// <param name="fromUtc">The inclusive start, UTC.</param>
    /// <param name="toUtc">The exclusive end, UTC.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The prices.</returns>
    private Task<PriceList> FetchAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken)
    {
        // The API takes whole hours. Round the end up rather than down so the request always covers the range
        // asked for; unrequested quarters must never be stored as unavailable.
        var startUtc = fromUtc.EnsureUtc().Floor();
        var endUtc = toUtc.EnsureUtc().Floor();

        if (endUtc < toUtc.EnsureUtc())
        {
            endUtc = endUtc.AddHours(1);
        }

        return _source.GetPricesAsync(startUtc, endUtc, cancellationToken);
    }
}
