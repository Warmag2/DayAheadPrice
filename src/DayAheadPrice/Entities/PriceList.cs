namespace DayAheadPrice.Entities;

/// <summary>
/// Entity which contains the price list.
/// </summary>
[Serializable]
public class PriceList
{
    /// <summary>
    /// List of prices per hour.
    /// </summary>
    public SortedList<DateTime, double> Prices { get; set; } = new();

    /// <summary>
    /// Gets electricity prices adjusted by VAT and operator margin.
    /// </summary>
    /// <param name="margin">Operator margin (c/kWh).</param>
    /// <param name="alv">VAT (simple multiplier, 24% => 0.24).</param>
    /// <returns>Sorted list with time and electricity price values (c/kWh).</returns>
    public SortedList<DateTime, double> GetPrices(decimal margin, decimal alv)
    {
        var adjustedPrices = new SortedList<DateTime, double>();

        foreach (var price in Prices)
        {
            adjustedPrices.Add(price.Key, price.Value * (double)(1m + alv) + (double)margin);
        }

        return adjustedPrices;
    }
}
