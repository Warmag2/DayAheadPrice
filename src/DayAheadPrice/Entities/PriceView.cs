using System;
using System.Collections.Generic;
using Faemiyah.DayAheadPrice.Enums;
using Faemiyah.DayAheadPrice.Logic;

namespace Faemiyah.DayAheadPrice.Entities;

/// <summary>
/// A rendered price window: the display bars plus everything the page needs to draw and navigate it.
/// </summary>
internal sealed class PriceView
{
    /// <summary>
    /// The display bars, keyed by UTC slot start, holding raw prices (before margin and VAT); <b>null</b> marks a bar
    /// whose period the source has no price for.
    /// </summary>
    public SortedList<DateTime, decimal?> Bars { get; init; } = [];

    /// <summary>
    /// The width of one bar.
    /// </summary>
    public CalendarStep Resolution { get; init; }

    /// <summary>
    /// The unit bars are grouped under for column headers.
    /// </summary>
    public CalendarUnit HeaderUnit { get; init; }

    /// <summary>
    /// The inclusive start of the window, UTC.
    /// </summary>
    public DateTime FromUtc { get; init; }

    /// <summary>
    /// The exclusive end of the window, UTC.
    /// </summary>
    public DateTime ToUtc { get; init; }

    /// <summary>
    /// The zoom level index this view was rendered at.
    /// </summary>
    public int ZoomIndex { get; init; }

    /// <summary>
    /// Whether a finer zoom level exists.
    /// </summary>
    public bool CanZoomIn => ZoomIndex > ZoomLevels.FinestIndex;

    /// <summary>
    /// Whether a coarser zoom level exists.
    /// </summary>
    public bool CanZoomOut => ZoomIndex < ZoomLevels.CoarsestIndex;

    /// <summary>
    /// Whether this is the live view (finest zoom, tracking the current time).
    /// </summary>
    public bool IsLive { get; init; }

    /// <summary>
    /// Whether there is stored data earlier than this window.
    /// </summary>
    public bool CanPanLeft { get; init; }

    /// <summary>
    /// Whether there is stored data later than this window.
    /// </summary>
    public bool CanPanRight { get; init; }

    /// <summary>
    /// Whether any bars are present.
    /// </summary>
    public bool HasData => Bars.Count > 0;
}
