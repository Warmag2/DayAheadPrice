using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Faemiyah.DayAheadPrice.Options;

/// <summary>
/// Configuration for electricity pricing.
/// </summary>
internal class PricingOptions : IValidatableObject
{
    /// <summary>
    /// The largest accepted margin magnitude, c/kWh.
    /// </summary>
    public const decimal MaxMarginMagnitude = 100m;

    /// <summary>
    /// The largest accepted VAT fraction.
    /// </summary>
    public const decimal MaxVat = 1m;

    /// <summary>
    /// The seller's margin in euro cents / kWh, VAT included: it is added to the final price as-is, without VAT.
    /// </summary>
    public decimal Margin { get; set; }

    /// <summary>
    /// Value added tax as a fraction of the pre-tax market price (0.255 => 25.5%). Not applied to <see cref="Margin"/>.
    /// </summary>
    public decimal Vat { get; set; }

    /// <summary>
    /// The final day average (c/kWh) above which the day summary raises an alert.
    /// </summary>
    public decimal AlertAverageThreshold { get; set; } = 10m;

    /// <summary>
    /// The final average of the day's most expensive hour (c/kWh) above which the day summary raises an alert.
    /// </summary>
    public decimal AlertHourThreshold { get; set; } = 20m;

    /// <summary>
    /// The final peak slot price (c/kWh) above which the day summary raises an alert.
    /// </summary>
    public decimal AlertMaxThreshold { get; set; } = 30m;

    /// <summary>
    /// Whether a margin is within the accepted range, [-<see cref="MaxMarginMagnitude"/>, <see cref="MaxMarginMagnitude"/>].
    /// </summary>
    /// <param name="margin">The margin, c/kWh.</param>
    /// <returns><b>True</b> when the margin is accepted.</returns>
    public static bool IsValidMargin(decimal margin) => Math.Abs(margin) <= MaxMarginMagnitude;

    /// <summary>
    /// Whether a VAT fraction is within the accepted range, [0, <see cref="MaxVat"/>].
    /// </summary>
    /// <param name="vat">The VAT fraction.</param>
    /// <returns><b>True</b> when the VAT is accepted.</returns>
    public static bool IsValidVat(decimal vat) => vat is >= 0m and <= MaxVat;

    /// <inheritdoc/>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!IsValidMargin(Margin))
        {
            yield return new ValidationResult($"PricingOptions:Margin must be within ±{MaxMarginMagnitude} c/kWh.", [nameof(Margin)]);
        }

        if (!IsValidVat(Vat))
        {
            yield return new ValidationResult($"PricingOptions:Vat must be a fraction within [0, {MaxVat}].", [nameof(Vat)]);
        }
    }
}
