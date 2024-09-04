namespace DayAheadPrice.Options;

/// <summary>
/// Configuration for electricity pricing.
/// </summary>
public class PricingOptions
{
    /// <summary>
    /// Margin in euro cents / kWh.
    /// </summary>
    public decimal Margin { get; set; }

    /// <summary>
    /// Value added tax as a fraction of total cost.
    /// </summary>
    public decimal Vat { get; set; }
}
