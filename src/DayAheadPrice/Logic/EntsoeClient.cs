using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Faemiyah.DayAheadPrice.Entities;
using Faemiyah.DayAheadPrice.Extensions;
using Faemiyah.DayAheadPrice.Options;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Faemiyah.DayAheadPrice.Logic;

/// <summary>
/// Reads day-ahead prices from the ENTSO-E Transparency Platform API.
/// </summary>
internal sealed class EntsoeClient : IPriceSource
{
    /// <summary>
    /// The name of the HTTP client used for the API (see <see cref="ServiceCollectionExtensions.AddPriceSource"/>).
    /// </summary>
    internal const string HttpClientName = "Entsoe";

    /// <summary>
    /// How much of a failed response's body is quoted in the exception message.
    /// </summary>
    private const int MaxErrorBodyLength = 500;

    private readonly EndpointOptions _endpointOptions;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<EntsoeClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="EntsoeClient"/> class.
    /// </summary>
    /// <param name="logger">The logging interface.</param>
    /// <param name="endpointOptions">The endpoint options.</param>
    /// <param name="httpClientFactory">The HTTP client factory, providing the <see cref="HttpClientName"/> client.</param>
    public EntsoeClient(ILogger<EntsoeClient> logger, IOptions<EndpointOptions> endpointOptions, IHttpClientFactory httpClientFactory)
    {
        _endpointOptions = endpointOptions.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<PriceList> GetPricesAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken)
    {
        var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        var url = QueryHelpers.AddQueryString(
            "api",
            new Dictionary<string, string?>
            {
                { "periodStart", FormatPeriod(fromUtc) },
                { "periodEnd", FormatPeriod(toUtc) },
                { "securityToken", _endpointOptions.ApiKey },
                { "documentType", _endpointOptions.DocumentType },
                { "contract_MarketAgreement.type", _endpointOptions.MarketAgreementType },
                { "in_Domain", _endpointOptions.Domain },
                { "out_Domain", _endpointOptions.Domain }
            });
        using var response = await httpClient.GetAsync(url, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        // "No data for this range" comes as an acknowledgement document, with either a success or an error status.
        // Any other failure (for example the HTML maintenance page served while the API is down) fails on the status
        // code here rather than surfacing as a confusing deserialization error. The start of the body goes into the
        // message, since that is where the API explains a rejection.
        if (!response.IsSuccessStatusCode && !EntsoeDocumentParser.IsNoMatchingData(body))
        {
            throw new HttpRequestException(
                $"ENTSO-E API returned {(int)response.StatusCode} ({response.ReasonPhrase}): {Truncate(body)}",
                null,
                response.StatusCode);
        }

        return EntsoeDocumentParser.Parse(body, _logger);
    }

    /// <summary>
    /// Shortens a response body for an error message.
    /// </summary>
    /// <param name="body">The response body.</param>
    /// <returns>At most the first <see cref="MaxErrorBodyLength"/> characters of the body.</returns>
    private static string Truncate(string body)
    {
        return body.Length <= MaxErrorBodyLength ? body : string.Concat(body.AsSpan(0, MaxErrorBodyLength), "…");
    }

    /// <summary>
    /// Formats an instant for the API's <c>periodStart</c>/<c>periodEnd</c> parameters, which are UTC.
    /// </summary>
    /// <param name="utc">The UTC instant.</param>
    /// <returns>The instant as <c>yyyyMMddHHmm</c>.</returns>
    private static string FormatPeriod(DateTime utc)
    {
        return utc.EnsureUtc().ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);
    }
}
