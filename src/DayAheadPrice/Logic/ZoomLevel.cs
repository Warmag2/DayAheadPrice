using Faemiyah.DayAheadPrice.Enums;

namespace Faemiyah.DayAheadPrice.Logic;

/// <summary>
/// A single zoom level: the bar resolution shown, how wide a window it covers, and how it aligns.
/// </summary>
/// <param name="Resolution">The width of one display bar.</param>
/// <param name="Span">The width of the whole window at this zoom (ignored by the live view).</param>
/// <param name="AlignUnit">The unit a non-live window start is floored to.</param>
/// <param name="HeaderUnit">The unit bars are grouped under for column headers.</param>
internal sealed record ZoomLevel(CalendarStep Resolution, CalendarStep Span, CalendarUnit AlignUnit, CalendarUnit HeaderUnit);
