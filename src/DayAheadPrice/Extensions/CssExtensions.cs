using System.Globalization;

namespace Faemiyah.DayAheadPrice.Extensions;

/// <summary>
/// Extensions for writing values into inline CSS.
/// </summary>
internal static class CssExtensions
{
    /// <summary>
    /// Format a number for CSS: culture-invariant, at most three decimals, and never empty.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The formatted value.</returns>
    public static string ToCss(this decimal value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
