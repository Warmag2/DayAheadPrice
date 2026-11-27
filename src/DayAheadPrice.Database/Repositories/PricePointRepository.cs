using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Faemiyah.DayAheadPrice.Database.Contexts;
using Faemiyah.DayAheadPrice.Database.Entities;
using Faemiyah.Repository.Interfaces;
using Faemiyah.Repository.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Faemiyah.DayAheadPrice.Database.Repositories;

/// <summary>
/// Repository for stored electricity price slots.
/// </summary>
public class PricePointRepository : TemporalRepositoryBase<PriceDbContext, long, PricePoint>
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
    /// Two plain <c>MIN</c>/<c>MAX</c> queries rather than one grouped aggregate: PostgreSQL answers each with a single
    /// index lookup (on <c>(Domain, DateBegin)</c> and <c>DateEnd</c>), so the cost doesn't grow with the history.
    /// This is asked on every positioned render and live-snapshot reload.
    /// </remarks>
    /// <param name="domain">The bidding zone / domain.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The (min start, max end) pair in UTC, or <b>null</b> when nothing is stored.</returns>
    public async Task<(DateTime MinUtc, DateTime MaxUtc)?> GetBoundsAsync(
        string domain,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await DatabaseContextProvider.GetDbContextAsync(cancellationToken: cancellationToken);

        var points = dbContext.Set<PricePoint>().AsNoTracking().Where(p => p.Domain == domain);

        // The selectors are nullable, so an empty domain gives null rather than throwing.
        var minUtc = await points.MinAsync(p => p.DateBegin, cancellationToken);
        var maxUtc = await points.MaxAsync(p => p.DateEnd, cancellationToken);

        if (minUtc is not { } min || maxUtc is not { } max)
        {
            return null;
        }

        return (min, max);
    }
}
