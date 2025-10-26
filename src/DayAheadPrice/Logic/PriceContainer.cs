using System.Drawing;
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
        var currentTimeStamp = DateTime.Now.Floor();

        if (currentTimeStamp > _lastUpdate)
        {
            try
            {
                _logger.LogInformation("Data timestamp ({last}) is older than current hour ({current}). Making new request.", _lastUpdate, currentTimeStamp);

                _currentPriceList = await MakePriceRequestAsync();
                _lastUpdate = currentTimeStamp;

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
    private static decimal ParsePrice(string price)
    {
        return decimal.Parse(price.Replace(",", string.Empty));
    }

    private PriceList MakePriceListFromResult(Publication_MarketDocument result)
    {
        var priceList = new PriceList();
        var now = DateTime.Now.Floor();
        var min = now.AddHours(-12);
        var max = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Local).AddDays(2);

        var prices = new SortedList<DateTime, decimal?>();

        try
        {
            // Only accept EUR
            foreach (var period in result.TimeSeries.Where(t => t.Currency == "EUR").SelectMany(t => t.Periods))
            {
                // Only read 15min resolution for this on
                if (period.Resolution == "PT15M")
                {
                    for (var timePosition = period.TimeInterval.StartDateTime; timePosition < period.TimeInterval.EndDateTime; timePosition += TimeSpan.FromMinutes(15))
                    {
                        prices.TryAdd(timePosition, null);
                    }

                    foreach (var point in period.Points)
                    {
                        var price = ParsePrice(point.Price) / 10;
                        var location = period.TimeInterval.StartDateTime.AddMinutes(15 * (point.Position - 1));

                        if (prices.ContainsKey(location) && prices[location].HasValue)
                        {
                            _logger.LogError("Attempting to add price where it already exists ({Time}): {Old}, {New}", location, prices[location], price);
                        }

                        prices[location] = price;
                    }


                    // Initialize the actual price list and fill any empty spots with their previous values
                    var lastPrice = 0m;

                    foreach (var point in prices)
                    {
                        if (!point.Value.HasValue)
                        {
                            AddToSeries(min, max, priceList, point.Key, lastPrice);
                        }
                        else
                        {
                            lastPrice = point.Value.Value;
                            AddToSeries(min, max, priceList, point.Key, lastPrice);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unknown exception while fetching price list: {Message}", ex.Message);
        }

        return priceList;
    }

    private void AddToSeries(DateTime min, DateTime max, PriceList priceList, DateTime timePosition, decimal price)
    {
        if (timePosition >= min && timePosition < max)
        {
            if (!priceList.Prices.ContainsKey(timePosition))
            {
                priceList.Prices.Add(timePosition, price);
            }
            else
            {
                _logger.LogError("Trying to add duplicate value for time {Time}. Existing: {OldPrice}, New: {NewPrice}", timePosition, priceList.Prices[timePosition], price);
            }
        }
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
            priceList.Prices.Add(time, _rand.Next(1000) / 50.0m);
        }

        return priceList;
    }

    private async Task<PriceList> MakePriceRequestAsync()
    {
        if (_endpointOptions.GenerateTestData)
        {
            var prices = new SortedList<DateTime, decimal>();

            for (var date = DateTime.UtcNow.AddDays(-0.5).Floor(); date < DateTime.UtcNow.AddDays(0.5).Floor(); date += TimeSpan.FromHours(1))
            {
                prices.Add(date, 1m * (decimal)Math.Sin(2 * Math.PI * date.Hour / 24));
            }

            return new PriceList
            {
                Prices = prices
            };
        }

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

        //var response1 = await httpClient.GetAsync($"https://web-api.tp.entsoe.eu/api?securityToken={_endpointOptions.ApiKey}&documentType={_endpointOptions.DocumentType}&outBiddingZone_Domain={_endpointOptions.Domain}&periodStart={GetDateTimeFormatString(DateTime.Now.AddDays(-1).Floor())}&periodEnd={GetDateTimeFormatString(DateTime.Now.AddDays(1).Floor())}");

        var response = await httpClient.SendAsync(request);

        var serializer = new XmlSerializer(typeof(Publication_MarketDocument));
        var xmlReaderSettings = new XmlReaderSettings()
        {
            DtdProcessing = DtdProcessing.Parse
        };

        var text = await response.Content.ReadAsStringAsync();

        var xmlReader = XmlReader.Create(await response.Content.ReadAsStreamAsync(), xmlReaderSettings);

        return MakePriceListFromResult(serializer.Deserialize(xmlReader) as Publication_MarketDocument ?? throw new InvalidDataException("Could not correctly deserialize data."));
    }
}
