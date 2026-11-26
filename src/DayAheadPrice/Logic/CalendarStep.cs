using System;
using Faemiyah.DayAheadPrice.Enums;
using Faemiyah.DayAheadPrice.Extensions;

namespace Faemiyah.DayAheadPrice.Logic;

/// <summary>
/// A calendar-aware step of a whole number of a given unit, used for bar widths and window spans so that months and
/// years keep their real (variable) length instead of a fixed number of days.
/// </summary>
/// <remarks>
/// Steps take and return UTC instants but follow the calendar of the time zone passed in. Day and coarser steps move
/// between local midnights, so a day is 23 or 25 hours long across a daylight-saving change. Sub-day steps move to the
/// next instant that is aligned to the step on the local clock: a repeated hour is visited twice and a skipped hour not
/// at all, while a multi-hour step stays on its local grid (a 4-hour bar spanning the change is 3 or 5 hours long).
/// </remarks>
/// <param name="Unit">The calendar unit stepped by.</param>
/// <param name="Count">The number of units in one step.</param>
public readonly record struct CalendarStep(CalendarUnit Unit, int Count)
{
    /// <summary>
    /// The length of one <see cref="CalendarUnit.Quarter"/>.
    /// </summary>
    public static readonly TimeSpan QuarterLength = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Add one step to the given UTC instant.
    /// </summary>
    /// <param name="utc">The instant to advance.</param>
    /// <param name="zone">The time zone whose calendar is followed.</param>
    /// <returns>The advanced instant, UTC.</returns>
    public DateTime AddTo(DateTime utc, TimeZoneInfo zone) => Shift(utc, zone, true);

    /// <summary>
    /// Subtract one step from the given UTC instant.
    /// </summary>
    /// <param name="utc">The instant to move back.</param>
    /// <param name="zone">The time zone whose calendar is followed.</param>
    /// <returns>The moved instant, UTC.</returns>
    public DateTime SubtractFrom(DateTime utc, TimeZoneInfo zone) => Shift(utc, zone, false);

    /// <summary>
    /// An approximate fixed-length view of this step, used only for rough centring when zooming.
    /// </summary>
    public TimeSpan Approximate => Unit switch
    {
        CalendarUnit.Minute => TimeSpan.FromMinutes(Count),
        CalendarUnit.Quarter => QuarterLength * Count,
        CalendarUnit.Hour => TimeSpan.FromHours(Count),
        CalendarUnit.Day => TimeSpan.FromDays(Count),
        CalendarUnit.Month => TimeSpan.FromDays(30 * Count),
        CalendarUnit.Year => TimeSpan.FromDays(365 * Count),
        _ => TimeSpan.FromDays(Count),
    };

    private DateTime Shift(DateTime utc, TimeZoneInfo zone, bool forward)
    {
        utc = utc.EnsureUtc();
        var count = forward ? Count : -Count;

        switch (Unit)
        {
            case CalendarUnit.Day:
                return utc.ToLocalWall(zone).AddDays(count).LocalToUtc(zone, !forward);
            case CalendarUnit.Month:
                return utc.ToLocalWall(zone).AddMonths(count).LocalToUtc(zone, !forward);
            case CalendarUnit.Year:
                return utc.ToLocalWall(zone).AddYears(count).LocalToUtc(zone, !forward);
        }

        var step = Unit switch
        {
            CalendarUnit.Minute => TimeSpan.FromMinutes(Count),
            CalendarUnit.Quarter => QuarterLength * Count,
            CalendarUnit.Hour => TimeSpan.FromHours(Count),
            _ => throw new NotImplementedException($"Stepping by unit {Unit} is not implemented yet.")
        };

        if (step <= TimeSpan.Zero)
        {
            throw new InvalidOperationException($"Cannot step by a non-positive count {Count}.");
        }

        // Two candidates for the next local-aligned instant: the same distance in real time, valid only if it lands
        // on the same local phase (it does except across a clock change), and the same distance on the wall clock.
        // The nearer one in the step direction wins.
        var wall = utc.ToLocalWall(zone);
        var real = forward ? utc + step : utc - step;
        var byWall = (forward ? wall + step : wall - step).LocalToUtc(zone, !forward);
        var realAligned = Phase(real.ToLocalWall(zone), step) == Phase(wall, step);

        if (forward)
        {
            var best = byWall > utc ? byWall : DateTime.MaxValue;
            return realAligned && real < best ? real : best;
        }
        else
        {
            var best = byWall < utc ? byWall : DateTime.MinValue;
            return realAligned && real > best ? real : best;
        }
    }

    private static long Phase(DateTime wall, TimeSpan step) => wall.TimeOfDay.Ticks % step.Ticks;
}
