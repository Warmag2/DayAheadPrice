using System.Globalization;
using System.Net;
using System.Xml;
using System.Xml.Serialization;
using DayAheadPrice.Entities;
using DayAheadPrice.Extensions;
using DayAheadPrice.Options;
using DayAheadPrice.Repositories;
using Microsoft.Extensions.Options;

namespace DayAheadPrice.Logic;

/// <summary>
/// Container and fetcher for electricity price data.
/// </summary>
internal class PriceContainer
{
    private const int MinimumServerErrorCode = 500;
    private const int RefreshLookaheadHours = 12;
    private const int StoredResolutionMinutes = 15;
    private const string StoredCurrency = "EUR";

    private readonly EndpointOptions _endpointOptions;
    private readonly PersistenceOptions _persistenceOptions;
    private readonly PricePointRepository _repository;
    private readonly ILogger<PriceContainer> _logger;
    private DateTime _lastUpdate = DateTime.MinValue;
    private PriceList _currentPriceList = new();
    private readonly Random _rand = new();
    private DateTime _earliestBackfillUtc = DateTime.MaxValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="PriceContainer"/> class.
    /// </summary>
    /// <param name="logger">The logging endpoint.</param>
    /// <param name="endpointOptions">The endpoint options.</param>
    /// <param name="persistenceOptions">The persistence options.</param>
    /// <param name="repository">The price repository used when persistence is enabled.</param>
    public PriceContainer(
        ILogger<PriceContainer> logger,
        IOptions<EndpointOptions> endpointOptions,
        IOptions<PersistenceOptions> persistenceOptions,
        PricePointRepository repository)
    {
        _endpointOptions = endpointOptions.Value;
        _persistenceOptions = persistenceOptions.Value;
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Ensure the database holds current prices, fetching and persisting from the API only when the stored data
    /// no longer extends far enough into the future.
    /// </summary>
    /// <remarks>
    /// The lookahead rule mirrors the in-memory cache: while stored prices reach more than
    /// <see cref="RefreshLookaheadHours"/> hours ahead, no request is made, so on a normal day the first request
    /// happens only once tomorrow's prices are due (around 13:00 local). A no-op when persistence is disabled.
    /// </remarks>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task EnsureFreshAsync(CancellationToken cancellationToken = default)
    {
        if (!_persistenceOptions.Enabled)
        {
            return;
        }

        var bounds = await _repository.GetBoundsAsync(_endpointOptions.Domain, cancellationToken);

        // Stored data still covers enough of the future; nothing new is likely to be published yet.
        if (bounds != null && bounds.Value.MaxUtc > DateTime.UtcNow.AddHours(RefreshLookaheadHours))
        {
            return;
        }

        // Throttle to at most one attempt per hour, so a failing or not-yet-updated API is not hammered.
        var currentHour = DateTime.UtcNow.Floor();

        if (currentHour <= _lastUpdate)
        {
            return;
        }

        _lastUpdate = currentHour;

        try
        {
            _logger.LogInformation("Stored prices are stale. Fetching new data from the API.");

            var priceList = await MakePriceRequestAsync();

            if (priceList.Prices.Count == 0)
            {
                _logger.LogError("ENTSO-e API returned a valid document containing no usable prices. Check the configured domain.");

                return;
            }

            await PersistAsync(priceList, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to fetch and persist price information.");
        }
    }

    /// <summary>
    /// Ensure stored prices reach back at least to <paramref name="fromUtc"/>, fetching and persisting any missing
    /// history from the API.
    /// </summary>
    /// <remarks>
    /// A no-op when persistence is disabled, when the requested start is already covered by stored data, or when an
    /// equally-early (or earlier) range has already been attempted this run — so a period the API has no data for is
    /// requested at most once.
    /// </remarks>
    /// <param name="fromUtc">The desired earliest slot start, UTC.</param>
    /// <param name="toUtc">The end of the requested window, UTC, used only when nothing is stored yet.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task EnsureRangeAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
    {
        if (!_persistenceOptions.Enabled)
        {
            return;
        }

        var bounds = await _repository.GetBoundsAsync(_endpointOptions.Domain, cancellationToken);

        // Stored data already reaches back at least this far.
        if (bounds != null && bounds.Value.MinUtc <= fromUtc)
        {
            return;
        }

        // A range starting this early (or earlier) has already been attempted, so don't ask again.
        if (fromUtc >= _earliestBackfillUtc)
        {
            return;
        }

        _earliestBackfillUtc = fromUtc;

        // Only the older slice is missing: fetch from the requested start up to whatever we already hold.
        var fetchEndUtc = bounds?.MinUtc ?? toUtc;

        try
        {
            _logger.LogInformation("Backfilling prices for domain {Domain} from {From} to {To}.", _endpointOptions.Domain, fromUtc, fetchEndUtc);

            var priceList = await MakePriceRequestAsync(fromUtc.ToLocalTime(), fetchEndUtc.ToLocalTime());

            if (priceList.Prices.Count == 0)
            {
                _logger.LogInformation("The API returned no prices for the requested history range.");

                return;
            }

            await PersistAsync(priceList, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to backfill historical price information.");
        }
    }

    private async Task PersistAsync(PriceList priceList, CancellationToken cancellationToken)
    {
        var points = new List<PricePoint>(priceList.Prices.Count);

        foreach (var (localTime, price) in priceList.Prices)
        {
            points.Add(new PricePoint
            {
                Domain = _endpointOptions.Domain,
                Currency = StoredCurrency,
                Price = price,
                DateBegin = localTime.ToUniversalTime(),
                DateEnd = localTime.AddMinutes(StoredResolutionMinutes).ToUniversalTime()
            });
        }

        await _repository.UpsertManyAsync(points, cancellationToken);

        _logger.LogInformation("Persisted {Count} price slots for domain {Domain}.", points.Count, _endpointOptions.Domain);
    }

    /// <summary>
    /// Gets a price list.
    /// </summary>
    /// <returns>Current price list along with the outcome of the fetch.</returns>
    public async Task<PriceFetchResult> GetPriceListAsync()
    {
        var currentTimeStamp = DateTime.UtcNow.Floor();

        // Early exit so we don't spam the API
        if (currentTimeStamp + TimeSpan.FromHours(12) < _currentPriceList.DateEnd)
        {
            return new PriceFetchResult(_currentPriceList, PriceFetchStatus.Success);
        }

        if (currentTimeStamp > _lastUpdate)
        {
            try
            {
                _logger.LogInformation("Data timestamp ({Last}) is older than current hour ({Current}). Making new request.", _lastUpdate, currentTimeStamp);

                var priceList = await MakePriceRequestAsync();

                if (priceList.Prices.Count == 0)
                {
                    _logger.LogError("ENTSO-e API returned a valid document containing no usable prices. Check the configured domain.");

                    return MakeFailureResult(PriceFetchStatus.NoDataForPeriod);
                }

                _currentPriceList = priceList;
                _lastUpdate = currentTimeStamp;

                return new PriceFetchResult(_currentPriceList, PriceFetchStatus.Success);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Unable to fetch price information. Status code: {StatusCode}", ex.StatusCode);

                return MakeFailureResult(ClassifyHttpFailure(ex));
            }
            catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or XmlException or FormatException)
            {
                _logger.LogError(ex, "Unable to read the price information response.");

                return MakeFailureResult(PriceFetchStatus.InvalidResponse);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unable to fetch price information.");

                return MakeFailureResult(PriceFetchStatus.UnknownError);
            }
        }
        else
        {
            return new PriceFetchResult(_currentPriceList, PriceFetchStatus.Success);
        }
    }

    /// <summary>
    /// Maps a failed request to the status shown to the user.
    /// </summary>
    /// <param name="exception">The exception thrown by the request.</param>
    /// <returns>The matching status.</returns>
    /// <remarks>
    /// Authentication failures deliberately fall through to <see cref="PriceFetchStatus.UnknownError"/>
    /// so that the page never reveals anything about the API key.
    /// </remarks>
    private static PriceFetchStatus ClassifyHttpFailure(HttpRequestException exception)
    {
        return exception.StatusCode switch
        {
            HttpStatusCode.NotFound => PriceFetchStatus.ApiNotFound,
            HttpStatusCode statusCode when (int)statusCode >= MinimumServerErrorCode => PriceFetchStatus.ApiUnavailable,
            null => PriceFetchStatus.Unreachable,
            _ => PriceFetchStatus.UnknownError
        };
    }

    /// <summary>
    /// Builds a failure result, keeping the cached prices when they are still worth showing.
    /// </summary>
    /// <param name="status">The failure status.</param>
    /// <returns>The failure result.</returns>
    /// <remarks>
    /// Prices are only fetched when the page is opened, so the cache can be arbitrarily old. It is
    /// only useful while it still covers time that has not passed yet.
    /// </remarks>
    private PriceFetchResult MakeFailureResult(PriceFetchStatus status)
    {
        // Price keys are local time, since the API reports UTC which gets parsed into local time.
        var hasFuturePrices = _currentPriceList.DateEnd > DateTime.Now;

        return new PriceFetchResult(hasFuturePrices ? _currentPriceList : null, status);
    }

    private static string GetDateTimeFormatString(DateTime dateTime)
    {
        return $"{dateTime:yyyyMMddHHmm}";
    }

    /// <summary>
    /// Parse the price information from ENTSO-e reported number format.
    /// Has to be done this way since the API returns something idiotic.
    /// </summary>
    /// <param name="price">The number as string.</param>
    /// <returns>The number as double.</returns>
    private static decimal ParsePrice(string price)
    {
        return decimal.Parse(price, CultureInfo.InvariantCulture); //.Replace(",", string.Empty)
    }

    private PriceList MakePriceListFromResult(Publication_MarketDocument result)
    {
        var priceList = new PriceList();

        var prices = new SortedList<DateTime, decimal?>();

        // Parse failures are intentionally left to propagate, so that the caller can report them as
        // a deserialization problem instead of them looking like the API returned no prices.
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

                    if (prices.TryGetValue(location, out var oldPrice) && oldPrice.HasValue)
                    {
                        _logger.LogError("Attempting to add price where it already exists ({Time}): {Old}, {New}", location, oldPrice, price);
                    }

                    prices[location] = price;
                }

                // Initialize the actual price list and fill any empty spots with their previous values
                var lastPrice = 0m;

                foreach (var point in prices)
                {
                    if (!point.Value.HasValue)
                    {
                        AddToSeries(priceList, point.Key, lastPrice);
                    }
                    else
                    {
                        lastPrice = point.Value.Value;
                        AddToSeries(priceList, point.Key, lastPrice);
                    }
                }
            }
        }

        return priceList;
    }

    private void AddToSeries(PriceList priceList, DateTime timePosition, decimal price)
    {
        if (priceList.Prices.TryGetValue(timePosition, out var oldPrice))
        {
            if (oldPrice != price)
            {
                _logger.LogError("Trying to add duplicate differing value for time {Time}. Existing: {OldPrice}, New: {NewPrice}", timePosition, oldPrice, price);
            }
        }
        else
        {
            priceList.Prices.Add(timePosition, price);
        }
    }

    /// <summary>
    /// Price list for testing and when the API is not available.
    /// </summary>
    /// <param name="startLocal">The inclusive local start of the generated range.</param>
    /// <param name="endLocal">The exclusive local end of the generated range.</param>
    /// <returns>A testing price list.</returns>
    private PriceList GenerateTestPrices(DateTime startLocal, DateTime endLocal)
    {
        var prices = new SortedList<DateTime, decimal>();

        for (var date = startLocal.Floor(); date < endLocal.Floor(); date += TimeSpan.FromMinutes(15))
        {
            if (_rand.NextDouble() < 0.9)
            {
                prices.Add(date, 1m * (decimal)Math.Sin(2 * Math.PI * date.Hour / 24));
            }
            else
            {
                prices.Add(date, 0m);
            }
        }

        return new PriceList
        {
            Prices = prices
        };
    }

    private PriceList GenerateTestPrices2()
    {
        var prices = new SortedList<DateTime, decimal>();

        for (var date = DateTime.UtcNow.AddDays(-1).Floor(); date < DateTime.UtcNow.AddDays(0.5).Floor(); date += TimeSpan.FromMinutes(15))
        {
            if(_rand.NextDouble() < 0.25)
            {
                prices.Add(date, _rand.Next(4) * 1m);
            }
            else
            {
                prices.Add(date, 3m * (decimal)_rand.NextDouble());
            }
        }

        return new PriceList
        {
            Prices = prices
        };
    }

    private async Task<PriceList> MakePriceRequestAsync(DateTime? periodStartLocal = null, DateTime? periodEndLocal = null)
    {
        var startLocal = (periodStartLocal ?? DateTime.Now.AddDays(-1)).Floor();
        var endLocal = (periodEndLocal ?? DateTime.Now.AddDays(1)).Floor();

        if (_endpointOptions.GenerateTestData)
        {
            return GenerateTestPrices(startLocal, endLocal);
        }

        using HttpClient httpClient = new();
        httpClient.BaseAddress = new Uri(_endpointOptions.ApiAddress);
        var url = Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(
            "api",
            new Dictionary<string, string?>
            {
                { "periodStart", GetDateTimeFormatString(startLocal) },
                { "periodEnd", GetDateTimeFormatString(endLocal) },
                { "securityToken", _endpointOptions.ApiKey },
                { "documentType", _endpointOptions.DocumentType },
                { "in_Domain", _endpointOptions.Domain },
                { "out_Domain", _endpointOptions.Domain }
            });
        var response = await httpClient.GetAsync(url);

        // The API serves an HTML maintenance page instead of XML while it is down, so fail on the
        // status code here rather than letting it surface as a confusing deserialization error.
        response.EnsureSuccessStatusCode();

        // Debug line for sending raw query
        //var response = await httpClient.GetAsync($"https://web-api.tp.entsoe.eu/api?securityToken={_endpointOptions.ApiKey}&documentType={_endpointOptions.DocumentType}&in_Domain={_endpointOptions.Domain}&out_Domain={_endpointOptions.Domain}&periodStart={GetDateTimeFormatString(DateTime.Now.AddDays(-1).Floor())}&periodEnd={GetDateTimeFormatString(DateTime.Now.AddDays(1).Floor())}"); //202511010000&periodEnd=202511012345");

        var serializer = new XmlSerializer(typeof(Publication_MarketDocument));
        var xmlReaderSettings = new XmlReaderSettings()
        {
            DtdProcessing = DtdProcessing.Ignore
        };

        // Debug line at seeing raw response
        //var text = await response.Content.ReadAsStringAsync();

        var xmlReader = XmlReader.Create(await response.Content.ReadAsStreamAsync(), xmlReaderSettings);

        return MakePriceListFromResult(serializer.Deserialize(xmlReader) as Publication_MarketDocument ?? throw new InvalidDataException("Could not correctly deserialize data."));
    }
}
