using System;
using System.Collections.Generic;
using System.Linq;

namespace Faemiyah.DayAheadPrice.Logic;

/// <summary>
/// Selects grid scales based on maximum and minimum value.
/// </summary>
internal static class GridScaleSelector
{
    /// <summary>
    /// Get the chart axis for a set of prices: a range that always includes zero, rounded out to whole grid steps.
    /// </summary>
    /// <remarks>
    /// Unavailable (<b>null</b>) prices are ignored. When there is nothing to span (no prices, or all zero), the axis
    /// is 0..1, so the result can always be divided by. Draw the grid with the returned scale: recomputing it from the
    /// rounded axis can pick a coarser scale whose lines miss the axis ends.
    /// </remarks>
    /// <param name="prices">The prices shown.</param>
    /// <returns>
    /// The axis top and bottom, and the grid scale they were rounded to; <c>Max</c> is always greater than <c>Min</c>.
    /// </returns>
    public static (decimal Max, decimal Min, decimal GridScale) GetAxis(IEnumerable<decimal?> prices)
    {
        var max = 0m;
        var min = 0m;

        foreach (var price in prices.Where(p => p.HasValue).Select(p => p!.Value))
        {
            max = Math.Max(max, price);
            min = Math.Min(min, price);
        }

        if (max == min)
        {
            max = 1m;
        }

        var gridScale = GetGridScale(Math.Ceiling(max), Math.Floor(min));

        return (Math.Ceiling(max / gridScale) * gridScale, Math.Floor(min / gridScale) * gridScale, gridScale);
    }

    /// <summary>
    /// Get the grid scale for the given min and max values.
    /// </summary>
    /// <param name="max">The maximum.</param>
    /// <param name="min">The minimum; must be less than <paramref name="max"/>.</param>
    /// <returns>An optimal grid scale.</returns>
    public static decimal GetGridScale(decimal max, decimal min)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(max, min);

        var baseHeight = (max - min) / 10m;
        var magnitude = (decimal)Math.Pow(10, Math.Ceiling(Math.Log10((double)baseHeight)) - 1);

        // Normally in (1, 10]; floating-point error in Log10 can push it to exactly 1, which must not yield 0.
        var normalized = baseHeight / magnitude;

        var step = normalized switch
        {
            <= 1 => 1,
            <= 2 => 2,
            <= 5 => 5,
            _ => 10
        };

        return step * magnitude;
    }
}