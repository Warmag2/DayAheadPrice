namespace DayAheadPrice.Extensions;

/// <summary>
/// Extensions for datetime classes.
/// </summary>
public static class DateTimeExtensions
{
    /// <summary>
    /// Floors a datetime to the nearest hour.
    /// </summary>
    /// <param name="dateTime">The datetime to floor.</param>
    /// <returns>A time at the start of the current hour.</returns>
    public static DateTime Floor(this DateTime dateTime)
    {
        return new DateTime(dateTime.Year, dateTime.Month, dateTime.Day, dateTime.Hour, 0, 0, DateTimeKind.Local);
    }
}
