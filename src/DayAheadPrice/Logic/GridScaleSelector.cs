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
    /// is 0..1, so the result can always be divided by.
    /// </remarks>
    /// <param name="prices">The prices shown.</param>
    /// <returns>The axis top and bottom; <c>Max</c> is always greater than <c>Min</c>.</returns>
    public static (decimal Max, decimal Min) GetAxis(IEnumerable<decimal?> prices)
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

        return (Math.Ceiling(max / gridScale) * gridScale, Math.Floor(min / gridScale) * gridScale);
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
        var baseHeightexponent = Math.Ceiling(Math.Log10((double)baseHeight));
        var baseHeightNormalized = baseHeight / (decimal)Math.Pow(10, baseHeightexponent - 1);

        var returnScale = 0;

        if (baseHeightNormalized > 1)
        {
            returnScale = 2;
        }

        if (baseHeightNormalized > 2)
        {
            returnScale = 5;
        }

        if (baseHeightNormalized > 5)
        {
            returnScale = 10;
        }

        if (baseHeightNormalized >= 10) // It may happen that we get exactly 10
        {
            returnScale = 10;
        }

        var normalizedReturnScale = (decimal)(returnScale * Math.Pow(10, baseHeightexponent - 1));

        return normalizedReturnScale;
    }
}