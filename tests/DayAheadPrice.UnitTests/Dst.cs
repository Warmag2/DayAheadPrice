using System;

namespace Faemiyah.DayAheadPrice.UnitTests;

/// <summary>
/// A Helsinki local day on which the clocks change.
/// </summary>
public enum DstDay
{
    /// <summary>
    /// 2025-10-26, when the clocks go back and the day is 25 hours long.
    /// </summary>
    FallBack,

    /// <summary>
    /// 2026-03-29, when the clocks go forward and the day is 23 hours long.
    /// </summary>
    SpringForward
}

/// <summary>
/// The time zone and daylight-saving days the tests are written against.
/// </summary>
internal static class Dst
{
    public static TimeZoneInfo Helsinki { get; } = TimeZoneInfo.FindSystemTimeZoneById("Europe/Helsinki");

    public static DateTime Utc(int year, int month, int day, int hour = 0, int minute = 0) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    public static DateTime Wall(int year, int month, int day, int hour = 0, int minute = 0) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);

    /// <summary>
    /// The UTC bounds of a Helsinki local day on which the clocks change.
    /// </summary>
    /// <param name="day">The day.</param>
    /// <returns>The local midnight starting the day and the one ending it, both UTC.</returns>
    public static (DateTime StartUtc, DateTime EndUtc) Bounds(DstDay day) => day switch
    {
        DstDay.FallBack => (Utc(2025, 10, 25, 21), Utc(2025, 10, 26, 22)),
        _ => (Utc(2026, 3, 28, 22), Utc(2026, 3, 29, 21))
    };
}
