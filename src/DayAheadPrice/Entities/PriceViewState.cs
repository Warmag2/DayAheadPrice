using System;

namespace DayAheadPrice.Entities;

/// <summary>
/// The navigable state of the price explorer: which zoom level, and where in time.
/// </summary>
/// <param name="ZoomIndex">The index into <see cref="Logic.ZoomLevels.All"/>.</param>
/// <param name="AnchorLocal">
/// The desired window start in local time, before alignment, or <b>null</b> for the live view. A non-null
/// anchor is only meaningful together with a zoom index; the live view is the finest zoom with no anchor.
/// </param>
internal sealed record PriceViewState(int ZoomIndex, DateTime? AnchorLocal)
{
    /// <summary>
    /// The default live view: finest zoom, anchored to the current time.
    /// </summary>
    public static PriceViewState Live { get; } = new(Logic.ZoomLevels.FinestIndex, null);
}
