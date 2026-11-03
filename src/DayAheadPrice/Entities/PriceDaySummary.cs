using System;

namespace DayAheadPrice.Entities;

/// <summary>
/// A compact summary of one day's electricity prices, returned by the public summary endpoint.
/// </summary>
/// <remarks>
/// Prices are c/kWh and already include the configured default <see cref="Margin"/> and <see cref="Vat"/>, applied
/// the same way the UI applies them (<c>raw * (1 + Vat) + Margin</c>). <see cref="Average"/> is time-weighted across
/// the day's slots.
/// </remarks>
/// <param name="Domain">The bidding zone / domain the prices are for.</param>
/// <param name="Date">The local calendar day the summary covers.</param>
/// <param name="Currency">The ISO currency the prices are expressed in.</param>
/// <param name="Min">The lowest slot price of the day, including margin and VAT.</param>
/// <param name="Max">The highest slot price of the day, including margin and VAT.</param>
/// <param name="Average">The time-weighted average price across the day, including margin and VAT.</param>
/// <param name="Margin">The margin (c/kWh) added to every price.</param>
/// <param name="Vat">The VAT fraction applied to every price.</param>
internal sealed record PriceDaySummary(
    string Domain,
    DateOnly Date,
    string Currency,
    decimal Min,
    decimal Max,
    decimal Average,
    decimal Margin,
    decimal Vat);
