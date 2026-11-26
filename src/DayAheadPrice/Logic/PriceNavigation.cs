using System;
using Faemiyah.DayAheadPrice.Entities;
using Faemiyah.DayAheadPrice.Enums;
using Faemiyah.DayAheadPrice.Extensions;

namespace Faemiyah.DayAheadPrice.Logic;

/// <summary>
/// Pure navigation between price explorer views: panning along time and zooming between levels.
/// </summary>
internal static class PriceNavigation
{
    /// <summary>
    /// The state one pan step earlier than the current view: the window start moves to the previous alignment
    /// boundary (the previous midnight, or the previous month at the coarsest scale).
    /// </summary>
    /// <param name="view">The current view.</param>
    /// <param name="zone">The time zone whose calendar the boundaries follow.</param>
    /// <returns>The panned state.</returns>
    public static PriceViewState PanLeft(PriceView view, TimeZoneInfo zone)
    {
        var level = ZoomLevels.All[view.ZoomIndex];

        return new PriceViewState(view.ZoomIndex, PreviousBoundary(view.FromUtc, level.AlignUnit, zone));
    }

    /// <summary>
    /// The state one pan step later than the current view: the window start moves to the next alignment boundary
    /// (the next midnight, or the next month at the coarsest scale), snapping back to live once it reaches the
    /// present.
    /// </summary>
    /// <param name="view">The current view.</param>
    /// <param name="zone">The time zone whose calendar the boundaries follow.</param>
    /// <param name="nowUtc">The current time, UTC.</param>
    /// <returns>The panned state.</returns>
    public static PriceViewState PanRight(PriceView view, TimeZoneInfo zone, DateTime nowUtc)
    {
        var level = ZoomLevels.All[view.ZoomIndex];
        var newFrom = NextBoundary(view.FromUtc, level.AlignUnit, zone);

        if (view.ZoomIndex == ZoomLevels.FinestIndex && newFrom >= nowUtc)
        {
            return PriceViewState.Live;
        }

        return new PriceViewState(view.ZoomIndex, newFrom);
    }

    /// <summary>
    /// The state one zoom level finer, keeping the window centred where it is.
    /// </summary>
    /// <param name="view">The current view.</param>
    /// <returns>The zoomed state, or the current state when already at the finest level.</returns>
    public static PriceViewState ZoomIn(PriceView view)
    {
        return view.CanZoomIn ? Recentre(view, view.ZoomIndex - 1) : StateOf(view);
    }

    /// <summary>
    /// The state one zoom level coarser, keeping the window centred where it is.
    /// </summary>
    /// <param name="view">The current view.</param>
    /// <returns>The zoomed state, or the current state when already at the coarsest level.</returns>
    public static PriceViewState ZoomOut(PriceView view)
    {
        return view.CanZoomOut ? Recentre(view, view.ZoomIndex + 1) : StateOf(view);
    }

    private static PriceViewState StateOf(PriceView view)
    {
        return new PriceViewState(view.ZoomIndex, view.IsLive ? null : view.FromUtc);
    }

    /// <summary>
    /// The local alignment boundary strictly before the given instant.
    /// </summary>
    /// <param name="utc">The instant, UTC.</param>
    /// <param name="unit">The alignment unit.</param>
    /// <param name="zone">The time zone whose calendar the boundaries follow.</param>
    /// <returns>The previous boundary, UTC.</returns>
    private static DateTime PreviousBoundary(DateTime utc, CalendarUnit unit, TimeZoneInfo zone)
    {
        var floored = utc.FloorLocal(unit, zone);

        return floored < utc ? floored : new CalendarStep(unit, 1).SubtractFrom(floored, zone);
    }

    /// <summary>
    /// The local alignment boundary strictly after the given instant.
    /// </summary>
    /// <param name="utc">The instant, UTC.</param>
    /// <param name="unit">The alignment unit.</param>
    /// <param name="zone">The time zone whose calendar the boundaries follow.</param>
    /// <returns>The next boundary, UTC.</returns>
    private static DateTime NextBoundary(DateTime utc, CalendarUnit unit, TimeZoneInfo zone)
    {
        return new CalendarStep(unit, 1).AddTo(utc.FloorLocal(unit, zone), zone);
    }

    private static PriceViewState Recentre(PriceView view, int newZoomIndex)
    {
        var centre = view.FromUtc + TimeSpan.FromTicks((view.ToUtc - view.FromUtc).Ticks / 2);
        var newSpan = ZoomLevels.All[newZoomIndex].Span;

        return new PriceViewState(newZoomIndex, centre - TimeSpan.FromTicks(newSpan.Approximate.Ticks / 2));
    }
}
