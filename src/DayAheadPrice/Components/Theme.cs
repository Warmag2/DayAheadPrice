namespace Faemiyah.DayAheadPrice.Components;

/// <summary>
/// The colour themes the pages support.
/// </summary>
internal static class Theme
{
    /// <summary>
    /// The theme used when none (or an unknown one) is requested.
    /// </summary>
    public const string Default = "dark";

    /// <summary>
    /// Resolve a requested theme name to a supported one.
    /// </summary>
    /// <param name="requested">The requested theme, for example from the "theme" query parameter.</param>
    /// <returns>"light" or "dark".</returns>
    public static string Resolve(string? requested) => requested is "light" or "dark" ? requested : Default;
}
