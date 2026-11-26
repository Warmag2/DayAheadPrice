using System.Globalization;

namespace Faemiyah.DayAheadPrice.Extensions;

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
    /// <param name="margin">The VAT-inclusive margin (c/kWh) to add after VAT.</param>
    /// <param name="vat">The VAT fraction to apply.</param>
    /// <returns>The final price, in the same unit as <paramref name="rawPrice"/>.</returns>
    public static decimal ApplyMarginAndVat(this decimal rawPrice, decimal margin, decimal vat)
    {
        return rawPrice * (1m + vat) + margin;
    }

    /// <summary>
    /// Format a price for display, such as <c>12.34c/kWh</c>.
    /// </summary>
    /// <param name="price">The price (c/kWh), or <b>null</b> when unavailable.</param>
    /// <returns>The formatted price, or <c>unavailable</c>.</returns>
    public static string FormatPrice(this decimal? price)
    {
        return price is { } value ? value.ToString("0.00", CultureInfo.InvariantCulture) + "c/kWh" : "unavailable";
    }
}
