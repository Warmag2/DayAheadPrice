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
    public static DateTime Floor(this DateTime dateTime, FloorResolution floorResolution = FloorResolution.Hour)
    {
        switch(floorResolution)
        {
            case FloorResolution.Minute:
                return new DateTime(dateTime.Year, dateTime.Month, dateTime.Day, dateTime.Hour, dateTime.Minute, 0, DateTimeKind.Local);
            case FloorResolution.Quarter:
                var minute = 0;

                if (dateTime.Minute >= 45)
                {
                    minute = 45;
                }
                else if (dateTime.Minute >= 30)
                {
                    minute = 30;
                }
                else if (dateTime.Minute >= 15)
                {
                    minute = 15;
                }

                return new DateTime(dateTime.Year, dateTime.Month, dateTime.Day, dateTime.Hour, minute, 0, DateTimeKind.Local);
            case FloorResolution.Hour:
                return new DateTime(dateTime.Year, dateTime.Month, dateTime.Day, dateTime.Hour, 0, 0, DateTimeKind.Local);
            case FloorResolution.Day:
                return new DateTime(dateTime.Year, dateTime.Month, dateTime.Day, 0, 0, 0, DateTimeKind.Local);
            default:
                throw new NotImplementedException($"Flooring for resolution {floorResolution} is not implemented yet.");
        }
    }
}
