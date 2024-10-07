using System.Xml;
using System.Xml.Serialization;
using DayAheadPrice.Entities;
using DayAheadPrice.Extensions;
using DayAheadPrice.Options;
using Microsoft.Extensions.Options;

namespace DayAheadPrice.Logic;

/// <summary>
/// Container and fetcher for electricity price data.
/// </summary>
public class PriceContainer
{
    private readonly Random _rand = new();
    private readonly EndpointOptions _endpointOptions;
    private readonly ILogger<PriceContainer> _logger;
    private DateTime _lastUpdate = DateTime.MinValue;
    private PriceList _currentPriceList = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="PriceContainer"/> class.
    /// </summary>
    /// <param name="logger">The logging endpoint.</param>
    /// <param name="endpointOptions">The endpoint options.</param>
    public PriceContainer(ILogger<PriceContainer> logger, IOptions<EndpointOptions> endpointOptions)
    {
        _endpointOptions = endpointOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// Gets a price list.
    /// </summary>
    /// <returns>Current price list.</returns>
    public async Task<PriceList?> GetPriceListAsync()
    {
        if (DateTime.Now.Floor() > _lastUpdate)
        {
            try
            {
                _currentPriceList = await MakePriceRequestAsync();
                _lastUpdate = DateTime.Now.Floor();

                return _currentPriceList;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unable to fetch price information.");

                return null;
            }
        }
        else
        {
            return _currentPriceList;
        }
    }

    private static string GetDateTimeFormatString(DateTime dateTime)
    {
        return $"{dateTime:yyyyMMddHHmm}";
    }

    /// <summary>
    /// Parse the price information from entso-e reported number format.
    /// Has to be done this way since the API returns something idiotic.
    /// </summary>
    /// <param name="price">The number as string.</param>
    /// <returns>The number as double.</returns>
    private static double ParsePrice(string price)
    {
        return double.Parse(price.Replace(",", string.Empty));
    }

    private PriceList MakePriceListFromResult(Publication_MarketDocument result)
    {
        var priceList = new PriceList();
        var now = DateTime.Now.Floor();
        var nextDayEnd = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Local).AddDays(2);

        // Only accept EUR
        foreach (var period in result.TimeSeries.Where(t => t.Currency == "EUR").Select(t => t.Period))
        {
            // Only read 60min resolution
            if (period.Resolution == "PT60M")
            {
                foreach (var point in period.Points)
                {
                    var timePosition = period.TimeInterval.StartDateTime.AddHours(point.Position - 1);

                    if (timePosition >= now.AddHours(-12) && timePosition < nextDayEnd)
                    {
                        if (!priceList.Prices.ContainsKey(timePosition))
                        {
                            priceList.Prices.Add(timePosition, ParsePrice(point.Price) / 10);
                        }
                        else
                        {
                            _logger.LogError("Trying to add duplicate value for time {Time}. Existing: {OldPrice}, New: {NewPrice}", timePosition, priceList.Prices[timePosition], ParsePrice(point.Price) / 10);
                        }
                    }
                }
            }
            else
            {
                _logger.LogInformation("Found data with resolution: {resolution}", period.Resolution);
            }
        }

        return priceList;
    }

    /// <summary>
    /// Price list for testing and when API is not available.
    /// </summary>
    /// <returns>A testing price list.</returns>
    private PriceList GetTestPriceList()
    {
        var priceList = new PriceList();

        var startDateTime = DateTime.Now.Floor().AddHours(-24);

        for (var time = startDateTime; time < startDateTime.AddHours(48); time += TimeSpan.FromHours(1))
        {
            priceList.Prices.Add(time, _rand.Next(1000) / 50.0d);
        }

        return priceList;
    }

    private async Task<PriceList> MakePriceRequestAsync()
    {
        using HttpClient httpClient = new();
        httpClient.BaseAddress = new Uri(_endpointOptions.BaseUrl);
        var request = new HttpRequestMessage(HttpMethod.Get, "api")
        {
            Content = new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    { "periodStart", GetDateTimeFormatString(DateTime.Now.AddDays(-1).Floor()) },
                    { "periodEnd", GetDateTimeFormatString(DateTime.Now.AddDays(1).Floor()) },
                    { "securityToken", _endpointOptions.ApiKey },
                    { "businessType", _endpointOptions.BusinessType },
                    { "documentType", _endpointOptions.DocumentType },
                    { "outBiddingZone_Domain", _endpointOptions.Domain },
                    { "processType", _endpointOptions.ProcessType },
                    { "In_Domain", _endpointOptions.Domain },
                    { "Out_Domain", _endpointOptions.Domain }
                })
        };

        //var response = await httpClient.GetAsync($"https://web-api.tp.entsoe.eu/api?securityToken={_endpointOptions.ApiKey}&documentType={_endpointOptions.DocumentType}&outBiddingZone_Domain={_endpointOptions.Domain}&periodStart={GetDateTimeFormatString(DateTime.Now.AddDays(-1).Floor())}&periodEnd={GetDateTimeFormatString(DateTime.Now.AddDays(1).Floor())}");

        var response = await httpClient.SendAsync(request);

        var serializer = new XmlSerializer(typeof(Publication_MarketDocument));
        var xmlReaderSettings = new XmlReaderSettings()
        {
            DtdProcessing = DtdProcessing.Parse
        };

        var xmlReader = XmlReader.Create(response.Content.ReadAsStream(), xmlReaderSettings);

        if (serializer.Deserialize(xmlReader) is not Publication_MarketDocument result)
        {
            return GetTestPriceList();
        }

        return MakePriceListFromResult(result);
    }
}
