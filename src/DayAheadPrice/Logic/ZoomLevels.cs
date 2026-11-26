using System.Collections.Generic;
using Faemiyah.DayAheadPrice.Enums;

namespace Faemiyah.DayAheadPrice.Logic;

/// <summary>
/// The ordered set of zoom levels, finest first.
/// </summary>
internal static class ZoomLevels
{
    /// <summary>
    /// The available zoom levels, index 0 being the finest (max zoom).
    /// </summary>
    public static IReadOnlyList<ZoomLevel> All { get; } =
    [
        new(new(CalendarUnit.Quarter, 1), new(CalendarUnit.Day, 1), CalendarUnit.Day, CalendarUnit.Hour),
        new(new(CalendarUnit.Hour, 1), new(CalendarUnit.Day, 7), CalendarUnit.Day, CalendarUnit.Day),
        new(new(CalendarUnit.Hour, 4), new(CalendarUnit.Day, 14), CalendarUnit.Day, CalendarUnit.Day),
        new(new(CalendarUnit.Day, 1), new(CalendarUnit.Month, 1), CalendarUnit.Month, CalendarUnit.Day),
        new(new(CalendarUnit.Month, 1), new(CalendarUnit.Year, 1), CalendarUnit.Year, CalendarUnit.Month)
    ];

    /// <summary>
    /// The finest zoom level index (maximum zoom).
    /// </summary>
    public const int FinestIndex = 0;

    /// <summary>
    /// The coarsest zoom level index (minimum zoom).
    /// </summary>
    public static int CoarsestIndex => All.Count - 1;
}
