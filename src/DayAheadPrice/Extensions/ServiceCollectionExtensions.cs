using System;
using Faemiyah.DayAheadPrice.Database.Contexts;
using Faemiyah.DayAheadPrice.Database.Repositories;
using Faemiyah.DayAheadPrice.Logic;
using Faemiyah.DayAheadPrice.Options;
using Faemiyah.Repository.Interfaces;
using Faemiyah.Repository.Options;
using Faemiyah.Repository.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Faemiyah.DayAheadPrice.Extensions;

/// <summary>
/// Service registrations shared by the app and its tests.
/// </summary>
internal static class ServiceCollectionExtensions
{
    // A month-long history chunk is the largest request; ENTSO-E answers it well within this.
    private static readonly TimeSpan ApiTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Register the price services and their options: persistence, ingestion, the live state and the series service.
    /// </summary>
    /// <remarks>
    /// The clock is <see cref="TimeProvider.System"/>; tests register their own <see cref="TimeProvider"/> afterwards
    /// to replace it.
    /// </remarks>
    /// <param name="services">The services.</param>
    /// <param name="configuration">The configuration holding the option sections.</param>
    /// <returns>The services.</returns>
    public static IServiceCollection AddPriceServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<EndpointOptions>().Bind(configuration.GetSection("EndpointOptions")).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<PricingOptions>().Bind(configuration.GetSection("PricingOptions")).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<DisplayOptions>().Bind(configuration.GetSection("DisplayOptions")).ValidateDataAnnotations().ValidateOnStart();
        services.Configure<SqlOptions>(configuration.GetSection("SqlOptions"));

        // The display time zone, shared by everything that aligns to or shows the local calendar.
        services.AddSingleton(sp => TimeZoneInfo.FindSystemTimeZoneById(sp.GetRequiredService<IOptions<DisplayOptions>>().Value.TimeZone));
        services.AddSingleton(TimeProvider.System);

        // Price persistence (Faemiyah.Repository-based PostgreSQL storage).
        services.AddSingleton<IConnectionStringAccessor, PgSqlConnectionStringAccessor>();
        services.AddSingleton<IDatabaseContextProvider<PriceDbContext>, PriceDbContextProvider>();
        services.AddSingleton<PricePointRepository>();

        services.AddSingleton<LivePriceState>();
        services.AddPriceSource();
        services.AddSingleton<PriceIngestionService>();
        services.AddSingleton<PriceSeriesService>();

        return services;
    }

    /// <summary>
    /// Register the <see cref="IPriceSource"/>: the ENTSO-E API, or generated prices when
    /// <see cref="EndpointOptions.GenerateTestData"/> is set.
    /// </summary>
    /// <param name="services">The services.</param>
    private static void AddPriceSource(this IServiceCollection services)
    {
        services.AddHttpClient(EntsoeClient.HttpClientName, (serviceProvider, client) =>
        {
            client.BaseAddress = new Uri(serviceProvider.GetRequiredService<IOptions<EndpointOptions>>().Value.ApiAddress);
            client.Timeout = ApiTimeout;
        });

        services.AddSingleton<IPriceSource>(serviceProvider =>
            serviceProvider.GetRequiredService<IOptions<EndpointOptions>>().Value.GenerateTestData
                ? ActivatorUtilities.CreateInstance<GeneratedPriceSource>(serviceProvider)
                : ActivatorUtilities.CreateInstance<EntsoeClient>(serviceProvider));
    }
}
