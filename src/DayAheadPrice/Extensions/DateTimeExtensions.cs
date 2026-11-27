using System;
using System.Globalization;
using System.Linq;
using Faemiyah.DayAheadPrice.Enums;

namespace Faemiyah.DayAheadPrice.Extensions;

/// <summary>
/// Extensions for datetime classes.
/// </summary>
/// <remarks>
/// Instants are carried as UTC throughout the app; the configured display time zone (see
/// <see cref="Options.DisplayOptions.TimeZone"/>) is used only for calendar alignment and display. The <c>*Local</c>
/// helpers here take and return UTC instants while doing their calendar work on that zone's wall clock, so
/// daylight-saving transitions are handled in one place.
/// </remarks>
internal static class DateTimeExtensions
{
    /// <summary>
    /// Floors a datetime to the start of the given calendar unit, on its own clock. The <see cref="DateTime.Kind"/> is
    /// preserved.
    /// </summary>
    /// <param name="dateTime">The <see cref="DateTime"/> to floor.</param>
    /// <param name="unit">The unit to floor to.</param>
    /// <returns>The floored <see cref="DateTime"/>.</returns>
    public static DateTime Floor(this DateTime dateTime, CalendarUnit unit = CalendarUnit.Hour)
    {
        return unit switch
        {
            CalendarUnit.Minute => new DateTime(dateTime.Year, dateTime.Month, dateTime.Day, dateTime.Hour, dateTime.Minute, 0, dateTime.Kind),
            CalendarUnit.Quarter => new DateTime(dateTime.Year, dateTime.Month, dateTime.Day, dateTime.Hour, dateTime.Minute - dateTime.Minute % 15, 0, dateTime.Kind),
            CalendarUnit.Hour => new DateTime(dateTime.Year, dateTime.Month, dateTime.Day, dateTime.Hour, 0, 0, dateTime.Kind),
            CalendarUnit.Day => new DateTime(dateTime.Year, dateTime.Month, dateTime.Day, 0, 0, 0, dateTime.Kind),
            CalendarUnit.Month => new DateTime(dateTime.Year, dateTime.Month, 1, 0, 0, 0, dateTime.Kind),
            CalendarUnit.Year => new DateTime(dateTime.Year, 1, 1, 0, 0, 0, dateTime.Kind),
            _ => throw new NotImplementedException($"Flooring for unit {unit} is not implemented yet.")
        };
    }

    /// <summary>
    /// Ceils a datetime to the start of the given calendar unit, on its own clock: a value already on a unit boundary
    /// is returned as is, anything else moves to the start of the next unit. The <see cref="DateTime.Kind"/> is
    /// preserved.
    /// </summary>
    /// <param name="dateTime">The <see cref="DateTime"/> to ceil.</param>
    /// <param name="unit">The unit to ceil to.</param>
    /// <returns>The ceiled <see cref="DateTime"/>.</returns>
    public static DateTime Ceiling(this DateTime dateTime, CalendarUnit unit = CalendarUnit.Hour)
    {
        var floored = dateTime.Floor(unit);

        if (floored == dateTime)
        {
            return floored;
        }

        return unit switch
        {
            CalendarUnit.Minute => floored.AddMinutes(1),
            CalendarUnit.Quarter => floored.AddMinutes(15),
            CalendarUnit.Hour => floored.AddHours(1),
            CalendarUnit.Day => floored.AddDays(1),
            CalendarUnit.Month => floored.AddMonths(1),
            _ => floored.AddYears(1)
        };
    }

    /// <summary>
    /// Floors a UTC instant to the start of the local calendar unit containing it.
    /// </summary>
    /// <remarks>
    /// Sub-day units are floored using the offset in force at the instant itself, so the two occurrences of a
    /// repeated hour (when the clocks go back) floor to two different instants. Day and coarser units floor the local
    /// date and resolve its start back to UTC.
    /// </remarks>
    /// <param name="utc">The UTC instant.</param>
    /// <param name="unit">The local unit to floor to.</param>
    /// <param name="zone">The time zone whose calendar is used.</param>
    /// <returns>The UTC instant at which the local unit starts.</returns>
    public static DateTime FloorLocal(this DateTime utc, CalendarUnit unit, TimeZoneInfo zone)
    {
        utc = utc.EnsureUtc();

        if (unit is CalendarUnit.Minute or CalendarUnit.Quarter or CalendarUnit.Hour)
        {
            var offset = zone.GetUtcOffset(utc);
            var floored = DateTime.SpecifyKind(utc + offset, DateTimeKind.Unspecified).Floor(unit);

            return DateTime.SpecifyKind(floored - offset, DateTimeKind.Utc);
        }

        return utc.ToLocalWall(zone).Floor(unit).LocalToUtc(zone);
    }

    /// <summary>
    /// The local wall-clock time of a UTC instant, as an <see cref="DateTimeKind.Unspecified"/> value suitable for
    /// calendar arithmetic and <see cref="LocalToUtc"/>.
    /// </summary>
    /// <param name="utc">The UTC instant.</param>
    /// <param name="zone">The time zone whose wall clock is read.</param>
    /// <returns>The local wall-clock time.</returns>
    public static DateTime ToLocalWall(this DateTime utc, TimeZoneInfo zone)
    {
        return DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(utc.EnsureUtc(), zone), DateTimeKind.Unspecified);
    }

    /// <summary>
    /// Resolve a local wall-clock time to a UTC instant.
    /// </summary>
    /// <remarks>
    /// A wall time repeated when the clocks go back resolves to its earlier occurrence, or to the later one when
    /// <paramref name="preferLater"/> is set. A wall time skipped when the clocks go forward does not exist; it is
    /// read with the standard offset, which lands just after the gap (the clock shifted forward by the gap).
    /// </remarks>
    /// <param name="local">The local wall-clock time; its <see cref="DateTime.Kind"/> is ignored.</param>
    /// <param name="zone">The time zone whose wall clock <paramref name="local"/> is on.</param>
    /// <param name="preferLater">Whether an ambiguous wall time resolves to its later occurrence.</param>
    /// <returns>The UTC instant.</returns>
    public static DateTime LocalToUtc(this DateTime local, TimeZoneInfo zone, bool preferLater = false)
    {
        var wall = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);

        TimeSpan offset;

        if (zone.IsAmbiguousTime(wall))
        {
            var offsets = zone.GetAmbiguousTimeOffsets(wall);
            offset = preferLater ? offsets.Min() : offsets.Max();
        }
        else
        {
            // For an invalid (skipped) time this is the zone's standard offset.
            offset = zone.GetUtcOffset(wall);
        }

        return DateTime.SpecifyKind(wall - offset, DateTimeKind.Utc);
    }

    /// <summary>
    /// Treat a value as a UTC instant, rejecting a local-kind value, which would indicate a time-handling bug.
    /// </summary>
    /// <param name="dateTime">The value, expected to be UTC.</param>
    /// <returns>The value with <see cref="DateTimeKind.Utc"/>.</returns>
    /// <exception cref="ArgumentException">The value is local time.</exception>
    public static DateTime EnsureUtc(this DateTime dateTime)
    {
        return dateTime.Kind switch
        {
            DateTimeKind.Utc => dateTime,
            DateTimeKind.Local => throw new ArgumentException("Expected a UTC instant but got local time.", nameof(dateTime)),
            _ => DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
        };
    }

    /// <summary>
    /// Describe a bar's time span in a compact, human-readable local form. A whole month reads as <c>yyyy-MM</c>, a
    /// whole day as <c>yyyy-MM-dd</c>, a multi-day span as a date range, and anything finer as a date with an
    /// hour-minute range.
    /// </summary>
    /// <param name="startUtc">The inclusive bar start, UTC.</param>
    /// <param name="endUtc">The exclusive bar end, UTC.</param>
    /// <param name="zone">The time zone the description is given in.</param>
    /// <returns>The formatted description.</returns>
    public static string DescribeSpan(DateTime startUtc, DateTime endUtc, TimeZoneInfo zone)
    {
        var start = startUtc.ToLocalWall(zone);
        var end = endUtc.ToLocalWall(zone);

        if (start == start.Floor(CalendarUnit.Month) && end == start.AddMonths(1))
        {
            return start.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        }

        if (start == start.Floor(CalendarUnit.Day) && end == start.AddDays(1))
        {
            return start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (end - start >= TimeSpan.FromDays(1))
        {
            return string.Create(CultureInfo.InvariantCulture, $"{start:MM-dd} \u2014 {end:MM-dd}");
        }

        return string.Create(CultureInfo.InvariantCulture, $"{start:MM-dd} \u23F2 {start:HH:mm} \u2014 {end:HH:mm}");
    }
}
