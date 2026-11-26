using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using AwesomeAssertions;
using Faemiyah.DayAheadPrice.Database.Contexts;
using Faemiyah.DayAheadPrice.Database.Entities;
using Faemiyah.DayAheadPrice.Database.Repositories;
using Faemiyah.DayAheadPrice.Extensions;
using Faemiyah.DayAheadPrice.Logic;
using Faemiyah.DayAheadPrice.Options;
using Faemiyah.Repository.Interfaces;
using Faemiyah.Repository.Options;
using Faemiyah.Repository.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Faemiyah.DayAheadPrice.IntegrationTests;

/// <summary>
/// Builds the price services on a fresh database in the shared container, without the web host.
/// </summary>
public static class TestServices
{
    /// <summary>
    /// The domain every test stores and reads.
    /// </summary>
    public const string Domain = "TEST";

    /// <summary>
    /// The display time zone the tests are written against.
    /// </summary>
    public static TimeZoneInfo Helsinki { get; } = TimeZoneInfo.FindSystemTimeZoneById("Europe/Helsinki");

    /// <summary>
    /// The pinned current time every test starts at: after all the fixed dates the tests use, so those are history.
    /// </summary>
    public static DateTime NowUtc { get; } = Utc(2026, 2, 16, 10, 20);

    /// <summary>
    /// Build the services. The API address points at a closed local port, so a test can never reach ENTSO-E. The
    /// clock is a <see cref="FakeTimeProvider"/> at <see cref="NowUtc"/>; resolve it to move time.
    /// </summary>
    /// <param name="generateTestData">Whether fetches generate synthetic prices instead of calling the API.</param>
    /// <param name="settings">Configuration values that override the test defaults.</param>
    /// <returns>The service provider. Dispose it after the test.</returns>
    public static ServiceProvider Create(bool generateTestData = false, IReadOnlyDictionary<string, string?>? settings = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Database"] = PostgreSqlFixture.BaseConnectionString,
            ["SqlOptions:DefaultDbName"] = "dayaheadprice_" + Guid.NewGuid().ToString("N"),
            ["EndpointOptions:ApiAddress"] = "http://127.0.0.1:9/",
            ["EndpointOptions:Domain"] = Domain,
            ["EndpointOptions:GenerateTestData"] = generateTestData.ToString(CultureInfo.InvariantCulture),
            ["PricingOptions:Margin"] = "0",
            ["PricingOptions:Vat"] = "0"
        };

        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            values[key] = value;
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var clock = new FakeTimeProvider(new DateTimeOffset(NowUtc));

        return new ServiceCollection()
            .AddLogging()
            .AddSingleton<IConfiguration>(configuration)
            .Configure<EndpointOptions>(configuration.GetSection("EndpointOptions"))
            .Configure<PricingOptions>(configuration.GetSection("PricingOptions"))
            .Configure<SqlOptions>(configuration.GetSection("SqlOptions"))
            .AddSingleton(Helsinki)
            .AddSingleton(clock)
            .AddSingleton<TimeProvider>(clock)
            .AddSingleton<IConnectionStringAccessor, PgSqlConnectionStringAccessor>()
            .AddSingleton<IDatabaseContextProvider<PriceDbContext>, PriceDbContextProvider>()
            .AddSingleton<PricePointRepository>()
            .AddSingleton<LivePriceState>()
            .AddPriceSource()
            .AddSingleton<PriceIngestionService>()
            .AddSingleton<PriceSeriesService>()
            .BuildServiceProvider();
    }

    /// <summary>
    /// Store a 15-minute slot for every quarter in the range.
    /// </summary>
    /// <param name="services">The services.</param>
    /// <param name="fromUtc">The first slot start, UTC.</param>
    /// <param name="toUtc">The exclusive end, UTC.</param>
    /// <param name="price">The price for a slot, given its start; <b>null</b> stores the slot as unavailable.</param>
    /// <returns>A task.</returns>
    public static async Task SeedQuartersAsync(this IServiceProvider services, DateTime fromUtc, DateTime toUtc, Func<DateTime, decimal?> price)
    {
        var points = new List<PricePoint>();

        for (var slotUtc = fromUtc; slotUtc < toUtc; slotUtc = slotUtc.AddMinutes(15))
        {
            points.Add(Slot(slotUtc, price(slotUtc)));
        }

        await services.GetRequiredService<PricePointRepository>().StoreAsync(points);
    }

    /// <summary>
    /// Store the given slots, overwriting any already stored for the same domain and start.
    /// </summary>
    /// <param name="repository">The repository.</param>
    /// <param name="points">The slots.</param>
    /// <returns>A task.</returns>
    public static async Task StoreAsync(this PricePointRepository repository, IReadOnlyCollection<PricePoint> points)
    {
        var result = await repository.AddOrUpdateManyAsync(
            points,
            incoming => p => p.Domain == incoming.Domain && p.DateBegin == incoming.DateBegin);

        result.IsSuccess.Should().BeTrue();
    }

    /// <summary>
    /// A 15-minute slot for the test domain.
    /// </summary>
    /// <param name="startUtc">The slot start, UTC.</param>
    /// <param name="price">The price; <b>null</b> for an unavailable slot.</param>
    /// <param name="domain">The domain.</param>
    /// <returns>The slot.</returns>
    public static PricePoint Slot(DateTime startUtc, decimal? price, string domain = Domain) => new()
    {
        Domain = domain,
        Currency = Market.Currency,
        Price = price,
        DateBegin = startUtc,
        DateEnd = startUtc + Market.SlotLength
    };

    public static DateTime Utc(int year, int month, int day, int hour = 0, int minute = 0) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);
}
