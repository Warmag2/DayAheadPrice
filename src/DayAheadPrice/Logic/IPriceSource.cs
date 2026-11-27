using System;
using System.Threading;
using System.Threading.Tasks;
using Faemiyah.DayAheadPrice.Entities;

namespace Faemiyah.DayAheadPrice.Logic;

/// <summary>
/// A source of published day-ahead prices.
/// </summary>
internal interface IPriceSource
{
    /// <summary>
    /// Get the published prices for the 15-minute slots in [<paramref name="fromUtc"/>, <paramref name="toUtc"/>).
    /// </summary>
    /// <param name="fromUtc">The inclusive start, UTC, on a whole hour.</param>
    /// <param name="toUtc">The exclusive end, UTC, on a whole hour.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The prices in c/kWh, keyed by slot start; slots with no published price are absent.</returns>
    Task<PriceList> GetPricesAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken);
}
