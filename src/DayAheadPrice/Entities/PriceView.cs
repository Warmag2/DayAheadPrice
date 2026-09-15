using System;
using System.Collections.Generic;
using DayAheadPrice.Enums;
using DayAheadPrice.Logic;

namespace DayAheadPrice.Entities;

/// <summary>
/// A rendered price window: the display bars plus everything the page needs to draw and navigate it.
/// </summary>
internal sealed class PriceView
{
    /// <summary>
    /// The display bars, keyed by local slot start, holding raw prices (before margin and VAT).
    /// </summary>
    public SortedList<DateTime, decimal> Bars { get; init; } = [];

    /// <summary>
    /// The width of one bar.
    /// </summary>
    public CalendarStep Resolution { get; init; }

    /// <summary>
    /// The unit bars are grouped under for column headers.
    /// </summary>
    public CalendarUnit HeaderUnit { get; init; }

    /// <summary>
    /// The inclusive local start of the window.
    /// </summary>
    public DateTime FromLocal { get; init; }

    /// <summary>
    /// The exclusive local end of the window.
    /// </summary>
    public DateTime ToLocal { get; init; }

    /// <summary>
    /// The zoom level index this view was rendered at.
    /// </summary>
    public int ZoomIndex { get; init; }

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
