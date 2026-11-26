using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Faemiyah.DayAheadPrice.Entities;
using Faemiyah.DayAheadPrice.Extensions;

namespace Faemiyah.DayAheadPrice.Logic;

/// <summary>
/// Synthetic prices, for testing and when the API is not available.
/// </summary>
/// <remarks>
/// Prices follow a sine of the local hour. Every slot is derived from its own start time, so fetching the same slot
/// again yields the same price, as the real API does. About one slot in ten is scattered to zero by a Fibonacci hash
/// of the slot index.
/// </remarks>
internal sealed class GeneratedPriceSource : IPriceSource
{
    private readonly TimeZoneInfo _zone;

    /// <summary>
    /// Initializes a new instance of the <see cref="GeneratedPriceSource"/> class.
    /// </summary>
    /// <param name="zone">The display time zone, used to shape the prices by local hour.</param>
    public GeneratedPriceSource(TimeZoneInfo zone)
    {
        _zone = zone;
    }

    /// <inheritdoc/>
    public Task<PriceList> GetPricesAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken)
    {
        var prices = new SortedList<DateTime, decimal>();

        for (var slotUtc = fromUtc; slotUtc < toUtc; slotUtc += Market.SlotLength)
        {
            var slotIndex = (ulong)(slotUtc.Ticks / Market.SlotLength.Ticks);
            var zeroed = (unchecked(slotIndex * 0x9E3779B97F4A7C15UL) >> 32) % 10 == 0;

            prices.Add(slotUtc, zeroed ? 0m : (decimal)Math.Sin(2 * Math.PI * slotUtc.ToLocalWall(_zone).Hour / 24));
        }

        return Task.FromResult(new PriceList { Prices = prices });
    }
}
