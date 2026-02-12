using DayAheadPrice.Extensions;

namespace DayAheadPrice.Entities;

/// <summary>
/// Entity which contains the price list.
/// </summary>
[Serializable]
internal class PriceList
{
    private const int HoursBefore = 12;
    private const int HoursAfter = 48;

    /// <summary>
    /// List of prices per hour.
    /// </summary>
    public SortedList<DateTime, decimal> Prices { get; set; } = [];

    /// <summary>
    /// Gets electricity prices adjusted by VAT and operator margin.
    /// </summary>
    /// <param name="margin">Operator margin (c/kWh).</param>
    /// <param name="vat">VAT (simple multiplier, 25.5% => 0.255).</param>
    /// <returns>Sorted list with time and electricity price values (c/kWh).</returns>
    public SortedList<DateTime, decimal> GetPrices(decimal margin, decimal vat)
    {
        var minDate = DateTime.Now.Floor().AddHours(-HoursBefore);
        var maxDate = DateTime.Now.Floor().AddHours(HoursAfter);

        SortedList<DateTime, decimal> adjustedPrices = [];

        foreach (var price in Prices)
        {
            if (price.Key >= minDate && price.Key <= maxDate)
            {
                adjustedPrices.Add(price.Key, price.Value * (1m + vat) + margin);
            }
        }

        return adjustedPrices;
    }

    public DateTime DateBegin => Prices.Count > 0 ? Prices.Keys.Min() : DateTime.MinValue;

    public DateTime DateEnd => Prices.Count > 0 ? Prices.Keys.Max() : DateTime.MinValue;
}
