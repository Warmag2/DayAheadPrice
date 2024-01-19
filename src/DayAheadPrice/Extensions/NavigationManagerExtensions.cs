using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.WebUtilities;

namespace DayAheadPrice.Extensions;

/// <summary>
/// Extensions for navigation mangager.
/// </summary>
public static class NavigationManagerExtensions
{
    /// <summary>
    /// Try to get a query parameter.
    /// </summary>
    /// <typeparam name="TValue">The type of the query parameter.</typeparam>
    /// <param name="navManager">The navigation manager.</param>
    /// <param name="key">Parameter key.</param>
    /// <param name="value">Parameter value.</param>
    /// <returns><b>True</b> if the parameter was successfully extracted, <b>false</b> otherwise.</returns>
    public static bool TryGetQueryString<TValue>(this NavigationManager navManager, string key, out TValue? value)
    {
        var uri = navManager.ToAbsoluteUri(navManager.Uri);

        if (QueryHelpers.ParseQuery(uri.Query).TryGetValue(key, out var valueFromQueryString))
        {
            if (typeof(TValue) == typeof(int) && int.TryParse(valueFromQueryString, out var valueAsInt))
            {
                value = (TValue)(object)valueAsInt;
                return true;
            }

            if (typeof(TValue) == typeof(string))
            {
                value = (TValue)(object)valueFromQueryString.ToString();
                return true;
            }

            if (typeof(TValue) == typeof(decimal) && decimal.TryParse(valueFromQueryString, out var valueAsDecimal))
            {
                value = (TValue)(object)valueAsDecimal;
                return true;
            }
        }

        value = default;

        return false;
    }
}