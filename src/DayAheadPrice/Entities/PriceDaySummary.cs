using System;

namespace DayAheadPrice.Entities;

/// <summary>
/// A compact summary of one day's electricity prices, returned by the public summary endpoint.
/// </summary>
/// <remarks>
/// Prices are the raw market prices (c/kWh) exactly as stored, before any margin or VAT, which the UI applies
/// per-user at display time. <see cref="Average"/> is time-weighted across the day's slots.
/// </remarks>
/// <param name="Domain">The bidding zone / domain the prices are for.</param>
/// <param name="Date">The local calendar day the summary covers.</param>
/// <param name="Currency">The ISO currency the prices are expressed in.</param>
/// <param name="Min">The lowest slot price of the day.</param>
/// <param name="Max">The highest slot price of the day.</param>
/// <param name="Average">The time-weighted average price across the day.</param>
internal sealed record PriceDaySummary(
    string Domain,
    DateOnly Date,
    string Currency,
    decimal Min,
    decimal Max,
    decimal Average);
