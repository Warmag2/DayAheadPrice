namespace DayAheadPrice.Extensions;

/// <summary>
/// Extensions for turning raw market prices into the final price shown to the user.
/// </summary>
internal static class PriceExtensions
{
    /// <summary>
    /// Apply margin and VAT to a raw market price, producing the final consumer price.
    /// </summary>
    /// <remarks>
    /// This is the single place the final price is computed, so the UI and the API stay in agreement. The transform
    /// is affine and monotonic in <paramref name="rawPrice"/>, so applying it to an aggregate (such as a minimum,
    /// maximum or average) yields the same result as applying it to each underlying slot first.
    /// </remarks>
    /// <param name="rawPrice">The raw price (c/kWh) before margin and VAT.</param>
    /// <param name="margin">The margin (c/kWh) to add.</param>
    /// <param name="vat">The VAT fraction to apply.</param>
    /// <returns>The final price, in the same unit as <paramref name="rawPrice"/>.</returns>
    public static decimal ApplyMarginAndVat(this decimal rawPrice, decimal margin, decimal vat)
    {
        return (rawPrice * (1m + vat)) + margin;
    }
}
