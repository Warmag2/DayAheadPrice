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
    public SortedList<DateTime, decimal> Prices { get; set; } = new();

    /// <summary>
    /// The resolution of the price list, i.e. the time interval between each price point.
    /// </summary>
    public TimeSpan Resolution
    {
        get
        {
            if (Prices.Count < 2)
            {
                return TimeSpan.FromHours(1);
            }

            return Prices.Keys[1] - Prices.Keys[0];
        }
    }

    /// <summary>
    /// Gets electricity prices adjusted by VAT and operator margin.
    /// </summary>
    /// <param name="margin">Operator margin (c/kWh).</param>
    /// <param name="vat">VAT (simple multiplier, 25.5% => 0.255).</param>
    /// <returns>Sorted list with time and electricity price values (c/kWh).</returns>
    public SortedList<DateTime, decimal> GetPrices(decimal margin, decimal vat)
    {
        var adjustedPrices = new SortedList<DateTime, decimal>();

        foreach (var price in Prices)
        {
            adjustedPrices.Add(price.Key, price.Value * (1m + vat) + margin);
        }

        return adjustedPrices;
    }
}
