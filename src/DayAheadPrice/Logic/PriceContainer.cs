using System.Xml.Serialization;
using DayAheadPrice.Entities;
using DayAheadPrice.Extensions;
using Microsoft.Extensions.Options;

namespace DayAheadPrice.Logic;

/// <summary>
/// Container and fetcher for electricity price data.
/// </summary>
public class PriceContainer
{
    private readonly Random _rand = new();
    private readonly EndpointOptions _endpointOptions;
    private DateTime _lastUpdate = DateTime.MinValue;
    private PriceList _currentPriceList = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="PriceContainer"/> class.
    /// </summary>
    /// <param name="endpointOptions">The endpoint options.</param>
    public PriceContainer(IOptions<EndpointOptions> endpointOptions)
    {
        _endpointOptions = endpointOptions.Value;
    }

    /// <summary>
    /// Gets a price list.
    /// </summary>
    /// <returns>Current price list.</returns>
    public async Task<PriceList> GetPriceListAsync()
    {
        if (DateTime.Now.Floor() > _lastUpdate)
        {
            _lastUpdate = DateTime.Now.Floor();
            _currentPriceList = await MakePriceRequestAsync();

            return _currentPriceList;
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

    private static PriceList MakePriceListFromResult(Publication_MarketDocument result)
    {
        var priceList = new PriceList();
        var now = DateTime.Now;
        var nextDayEnd = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Local).AddDays(2);

        foreach (var period in result.TimeSeries.Select(t => t.Period))
        {
            foreach (var point in period.Points)
            {
                var timePosition = period.TimeInterval.StartDateTime.AddHours(point.Position - 1);

                if (timePosition >= DateTime.Now.Floor().AddHours(-12) && timePosition < nextDayEnd)
                {
                    priceList.Prices.Add(timePosition, point.Price / 10);
                }
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
                    { "documentType", "A44" },
                    { "In_Domain", _endpointOptions.Domain },
                    { "Out_Domain", _endpointOptions.Domain }
                })
        };

        var response = await httpClient.SendAsync(request);

        var serializer = new XmlSerializer(typeof(Publication_MarketDocument));

        if (serializer.Deserialize(response.Content.ReadAsStream()) is not Publication_MarketDocument result)
        {
            return GetTestPriceList();
        }

        return MakePriceListFromResult(result);
    }
}
