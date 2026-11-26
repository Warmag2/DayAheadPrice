using System;
using Faemiyah.DayAheadPrice.Logic;
using Faemiyah.DayAheadPrice.Options;
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
    /// Register the <see cref="IPriceSource"/>: the ENTSO-E API, or generated prices when
    /// <see cref="EndpointOptions.GenerateTestData"/> is set.
    /// </summary>
    /// <param name="services">The services.</param>
    /// <returns>The services.</returns>
    public static IServiceCollection AddPriceSource(this IServiceCollection services)
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

        return services;
    }
}
