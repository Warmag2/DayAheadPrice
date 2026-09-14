using DayAheadPrice.Enums;

namespace DayAheadPrice.Logic;

/// <summary>
/// A calendar-aware step of a whole number of a given unit, used for bar widths and window spans so that months and
/// years keep their real (variable) length instead of a fixed number of days.
/// </summary>
/// <param name="Unit">The calendar unit stepped by.</param>
/// <param name="Count">The number of units in one step.</param>
public readonly record struct CalendarStep(CalendarUnit Unit, int Count)
{
    /// <summary>
    /// Add one step to the given instant.
    /// </summary>
    /// <param name="dateTime">The instant to advance.</param>
    /// <returns>The advanced instant.</returns>
    public DateTime AddTo(DateTime dateTime) => Shift(dateTime, Count);

    /// <summary>
    /// Subtract one step from the given instant.
    /// </summary>
    /// <param name="dateTime">The instant to move back.</param>
    /// <returns>The moved instant.</returns>
    public DateTime SubtractFrom(DateTime dateTime) => Shift(dateTime, -Count);

    /// <summary>
    /// An approximate fixed-length view of this step, used only for rough centring when zooming.
    /// </summary>
    public TimeSpan Approximate => Unit switch
    {
        CalendarUnit.Minute => TimeSpan.FromMinutes(Count),
        CalendarUnit.Quarter => TimeSpan.FromMinutes(15 * Count),
        CalendarUnit.Hour => TimeSpan.FromHours(Count),
        CalendarUnit.Day => TimeSpan.FromDays(Count),
        CalendarUnit.Month => TimeSpan.FromDays(30 * Count),
        CalendarUnit.Year => TimeSpan.FromDays(365 * Count),
        _ => TimeSpan.FromDays(Count),
    };

    private DateTime Shift(DateTime dateTime, int count) => Unit switch
    {
        CalendarUnit.Minute => dateTime.AddMinutes(count),
        CalendarUnit.Quarter => dateTime.AddMinutes(15 * count),
        CalendarUnit.Hour => dateTime.AddHours(count),
        CalendarUnit.Day => dateTime.AddDays(count),
        CalendarUnit.Month => dateTime.AddMonths(count),
        CalendarUnit.Year => dateTime.AddYears(count),
        _ => dateTime.AddDays(count),
    };
}
