namespace DayAheadPrice.Enums;

/// <summary>
/// A calendar unit, used both for flooring instants and for grouping / aligning price windows.
/// </summary>
/// <remarks>
/// Ordered from finest to coarsest.
/// </remarks>
public enum CalendarUnit
{
    /// <summary>
    /// Whole minutes.
    /// </summary>
    Minute,

    /// <summary>
    /// Quarter hours (00, 15, 30, 45).
    /// </summary>
    Quarter,

    /// <summary>
    /// Whole hours.
    /// </summary>
    Hour,

    /// <summary>
    /// Whole days (local midnight).
    /// </summary>
    Day,

    /// <summary>
    /// Whole months (first of the month).
    /// </summary>
    Month,

    /// <summary>
    /// Whole years (first of January).
    /// </summary>
    Year
}
