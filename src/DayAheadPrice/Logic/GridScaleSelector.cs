namespace DayAheadPrice.Logic;

/// <summary>
/// Selects grid scales based on maximum and minimum value.
/// </summary>
internal static class GridScaleSelector
{
    /// <summary>
    /// Get the grid scale for the given min and max values.
    /// </summary>
    /// <param name="max">The maximum.</param>
    /// <param name="min">The minimum.</param>
    /// <returns>An optimal grid scale.</returns>
    public static decimal GetGridScale(decimal max, decimal min)
    {
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