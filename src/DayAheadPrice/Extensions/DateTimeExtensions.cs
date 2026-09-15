using System;
using DayAheadPrice.Enums;

namespace DayAheadPrice.Extensions;

/// <summary>
/// Extensions for datetime classes.
/// </summary>
internal static class DateTimeExtensions
{
    /// <summary>
    /// Floors a datetime to the start of the given calendar unit.
    /// </summary>
    /// <param name="dateTime">The <see cref="DateTime"/> to floor.</param>
    /// <param name="unit">The unit to floor to.</param>
    /// <returns>The floored <see cref="DateTime"/>.</returns>
    public static DateTime Floor(this DateTime dateTime, CalendarUnit unit = CalendarUnit.Hour)
    {
        switch(unit)
        {
            case CalendarUnit.Minute:
                return new DateTime(dateTime.Year, dateTime.Month, dateTime.Day, dateTime.Hour, dateTime.Minute, 0, DateTimeKind.Local);
            case CalendarUnit.Quarter:
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
            case CalendarUnit.Hour:
                return new DateTime(dateTime.Year, dateTime.Month, dateTime.Day, dateTime.Hour, 0, 0, DateTimeKind.Local);
            case CalendarUnit.Day:
                return new DateTime(dateTime.Year, dateTime.Month, dateTime.Day, 0, 0, 0, DateTimeKind.Local);
            case CalendarUnit.Month:
                return new DateTime(dateTime.Year, dateTime.Month, 1, 0, 0, 0, DateTimeKind.Local);
            case CalendarUnit.Year:
                return new DateTime(dateTime.Year, 1, 1, 0, 0, 0, DateTimeKind.Local);
            default:
                throw new NotImplementedException($"Flooring for unit {unit} is not implemented yet.");
        }
    }

    /// <summary>
    /// Describe a bar's time span in a compact, human-readable form. A whole month reads as <c>yyyy-MM</c>, a whole
    /// day as <c>yyyy-MM-dd</c>, a multi-day span as a date range, and anything finer as a date with an hour-minute
    /// range.
    /// </summary>
    /// <param name="start">The inclusive local bar start.</param>
    /// <param name="end">The exclusive local bar end.</param>
    /// <returns>The formatted description.</returns>
    public static string DescribeSpan(DateTime start, DateTime end)
    {
        if (start == start.Floor(CalendarUnit.Month) && end == start.AddMonths(1))
        {
            return $"{start.Year}-{start.Month.ToString("00")}";
        }

        if (start == start.Floor(CalendarUnit.Day) && end == start.AddDays(1))
        {
            return $"{start.Year}-{start.Month.ToString("00")}-{start.Day.ToString("00")}";
        }

        if (end - start >= TimeSpan.FromDays(1))
        {
            return $"{start.Month.ToString("00")}-{start.Day.ToString("00")} \u2014 {end.Month.ToString("00")}-{end.Day.ToString("00")}";
        }

        return $"{start.Month.ToString("00")}-{start.Day.ToString("00")} \u23F2 {start.Hour.ToString("00")}:{start.Minute.ToString("00")} \u2014 {end.Hour.ToString("00")}:{end.Minute.ToString("00")}";
    }
}
