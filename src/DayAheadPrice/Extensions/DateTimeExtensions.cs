namespace DayAheadPrice.Extensions;

/// <summary>
/// Extensions for datetime classes.
/// </summary>
internal static class DateTimeExtensions
{
    /// <summary>
    /// Floors a datetime to the nearest hour.
    /// </summary>
    /// <param name="dateTime">The <see cref="DateTime"> to floor.</param>
    /// <returns>The floored <see cref="DateTime"/>.</returns>
    public static DateTime Floor(this DateTime dateTime)
    {
        return new DateTime(dateTime.Year, dateTime.Month, dateTime.Day, dateTime.Hour, 0, 0, DateTimeKind.Local);
    }
}
