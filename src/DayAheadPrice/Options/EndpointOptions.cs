using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Faemiyah.DayAheadPrice.Options;

/// <summary>
/// Options for fetching data from the ENTSO-e API.
/// </summary>
internal class EndpointOptions : IValidatableObject
{
    /// <summary>
    /// API root URL.
    /// </summary>
    public string ApiAddress { get; set; } = string.Empty;

    /// <summary>
    /// API key.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Document type to ask.
    /// </summary>
    /// <remarks>
    /// Use default if you do not know what this means.
    /// </remarks>
    public string DocumentType { get; set; } = "A44";

    /// <summary>
    /// Domain to ask prices for.
    /// </summary>
    public string Domain { get; set; } = string.Empty;

    /// <summary>
    /// The human-readable name of the <see cref="Domain"/>'s region, shown in the page title.
    /// </summary>
    public string RegionDisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Generate test data instead of making API calls.
    /// </summary>
    public bool GenerateTestData { get; set; }

    /// <summary>
    /// Contract market agreement type, sent as <c>contract_MarketAgreement.type</c>.
    /// </summary>
    /// <remarks>
    /// <c>A01</c> selects the day-ahead auction. Without it the API may also return intraday-auction series, whose
    /// prices would be merged into the day-ahead ones. Use default if you do not know what this means.
    /// </remarks>
    public string MarketAgreementType { get; set; } = "A01";

    /// <inheritdoc/>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Uri.TryCreate(ApiAddress, UriKind.Absolute, out var address) || address.Scheme is not ("https" or "http"))
        {
            yield return new ValidationResult("EndpointOptions:ApiAddress must be an absolute http(s) URL.", [nameof(ApiAddress)]);
        }

        if (string.IsNullOrWhiteSpace(Domain))
        {
            yield return new ValidationResult("EndpointOptions:Domain is required.", [nameof(Domain)]);
        }

        if (!GenerateTestData && string.IsNullOrWhiteSpace(ApiKey))
        {
            yield return new ValidationResult("EndpointOptions:ApiKey is required unless GenerateTestData is set.", [nameof(ApiKey)]);
        }
    }
}
