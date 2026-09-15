using System.Collections.Generic;
using DayAheadPrice.Enums;

namespace DayAheadPrice.Logic;

/// <summary>
/// A single zoom level: the bar resolution shown, how wide a window it covers, and how it aligns.
/// </summary>
/// <param name="Resolution">The width of one display bar.</param>
/// <param name="Span">The width of the whole window at this zoom (ignored by the live view).</param>
/// <param name="AlignUnit">The unit a non-live window start is floored to.</param>
/// <param name="HeaderUnit">The unit bars are grouped under for column headers.</param>
internal sealed record ZoomLevel(CalendarStep Resolution, CalendarStep Span, CalendarUnit AlignUnit, CalendarUnit HeaderUnit);

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

    /// <summary>
    /// Whether the given zoom index can zoom in further (to a finer level).
    /// </summary>
    /// <param name="zoomIndex">The current zoom index.</param>
    /// <returns><b>True</b> if a finer level exists.</returns>
    public static bool CanZoomIn(this int zoomIndex) => zoomIndex > FinestIndex;

    /// <summary>
    /// Whether the given zoom index can zoom out further (to a coarser level).
    /// </summary>
    /// <param name="zoomIndex">The current zoom index.</param>
    /// <returns><b>True</b> if a coarser level exists.</returns>
    public static bool CanZoomOut(this int zoomIndex) => zoomIndex < CoarsestIndex;
}
