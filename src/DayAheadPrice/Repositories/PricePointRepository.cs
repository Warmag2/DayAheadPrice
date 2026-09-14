using DayAheadPrice.Data;
using DayAheadPrice.Entities;
using Microsoft.EntityFrameworkCore;
using RepositoryPrototype.Interfaces;
using RepositoryPrototype.Repositories;

namespace DayAheadPrice.Repositories;

/// <summary>
/// Repository for stored electricity price slots.
/// </summary>
internal class PricePointRepository : TemporalRepositoryBase<PriceDbContext, long, PricePoint>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PricePointRepository"/> class.
    /// </summary>
    /// <param name="logger">The logging interface.</param>
    /// <param name="databaseContextProvider">The database context provider.</param>
    /// <param name="timeProvider">The source of the current time, or <b>null</b> for the system clock.</param>
    public PricePointRepository(
        ILogger<PricePointRepository> logger,
        IDatabaseContextProvider<PriceDbContext> databaseContextProvider,
        TimeProvider? timeProvider = null)
        : base(logger, databaseContextProvider, timeProvider)
    {
    }

    /// <summary>
    /// Get every stored slot for a domain that overlaps the given UTC interval.
    /// </summary>
    /// <param name="domain">The bidding zone / domain.</param>
    /// <param name="fromUtc">The inclusive start of the interval, UTC.</param>
    /// <param name="toUtc">The exclusive end of the interval, UTC.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The overlapping slots.</returns>
    public async Task<IReadOnlyCollection<PricePoint>> GetRangeAsync(
        string domain,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        return await GetAllAsync(
            p => p.Domain == domain && p.DateEnd > fromUtc && p.DateBegin < toUtc,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Get the earliest slot start and latest slot end stored for a domain.
    /// </summary>
    /// <remarks>
    /// Uses a direct aggregate query rather than materialising the domain, since the history can grow large and
    /// this is asked on every page render to bound navigation.
    /// </remarks>
    /// <param name="domain">The bidding zone / domain.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The (min start, max end) pair in UTC, or <b>null</b> when nothing is stored.</returns>
    public async Task<(DateTime MinUtc, DateTime MaxUtc)?> GetBoundsAsync(
        string domain,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await DatabaseContextProvider.GetDbContextAsync(cancellationToken: cancellationToken);

        var query = dbContext.Set<PricePoint>().AsNoTracking().Where(p => p.Domain == domain);

        if (!await query.AnyAsync(cancellationToken))
        {
            return null;
        }

        var min = await query.MinAsync(p => p.DateBegin, cancellationToken);
        var max = await query.MaxAsync(p => p.DateEnd, cancellationToken);

        if (min == null || max == null)
        {
            return null;
        }

        return (min.Value, max.Value);
    }

    /// <summary>
    /// Insert the given slots, updating any that already exist for the same domain and start time.
    /// </summary>
    /// <remarks>
    /// Prices are written at most once per publication, so this simply adds or updates each slot in turn rather
    /// than batching. Slots are matched on (<see cref="Entities.PricePoint.Domain"/>,
    /// <see cref="Entities.PricePoint.DateBegin"/>), the unique index, so re-fetching a window overwrites rather
    /// than duplicates.
    /// </remarks>
    /// <param name="points">The slots to store. All must carry a <see cref="Entities.PricePoint.DateBegin"/>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task UpsertManyAsync(
        IReadOnlyCollection<PricePoint> points,
        CancellationToken cancellationToken = default)
    {
        foreach (var incoming in points)
        {
            var stored = await GetAsync(
                p => p.Domain == incoming.Domain && p.DateBegin == incoming.DateBegin,
                cancellationToken: cancellationToken);

            if (stored == null)
            {
                await AddAsync(incoming, cancellationToken);
            }
            else
            {
                stored.Price = incoming.Price;
                stored.DateEnd = incoming.DateEnd;
                stored.Currency = incoming.Currency;
                await UpdateAsync(stored, cancellationToken);
            }
        }
    }
}
