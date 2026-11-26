using System;
using System.IO;
using System.Threading;
using Faemiyah.Common.Hosting;
using Faemiyah.Common.Hosting.Logging;
using Faemiyah.DayAheadPrice.Components;
using Faemiyah.DayAheadPrice.Database.Contexts;
using Faemiyah.DayAheadPrice.Database.Repositories;
using Faemiyah.DayAheadPrice.Extensions;
using Faemiyah.DayAheadPrice.Logic;
using Faemiyah.DayAheadPrice.Options;
using Faemiyah.Repository.Interfaces;
using Faemiyah.Repository.Options;
using Faemiyah.Repository.Providers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Faemiyah.DayAheadPrice;

/// <summary>
/// Main program class.
/// </summary>
internal static class Program
{
    /// <summary>
    /// Main program entrypoint.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Faemiyah logging and JSON serializer defaults from the shared libraries.
        builder.Logging.AddFaemiyahLogging();
        builder.Services.ConfigureJsonSerializerOptions();

        // Add services to the container.
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();

        // Custom services
        builder.Services.AddOptions<EndpointOptions>().Bind(builder.Configuration.GetSection("EndpointOptions")).ValidateDataAnnotations().ValidateOnStart();
        builder.Services.AddOptions<PricingOptions>().Bind(builder.Configuration.GetSection("PricingOptions")).ValidateDataAnnotations().ValidateOnStart();
        builder.Services.AddOptions<DisplayOptions>().Bind(builder.Configuration.GetSection("DisplayOptions")).ValidateDataAnnotations().ValidateOnStart();
        builder.Services.Configure<SqlOptions>(builder.Configuration.GetSection("SqlOptions"));

        // The display time zone, shared by everything that aligns to or shows the local calendar.
        builder.Services.AddSingleton(sp => TimeZoneInfo.FindSystemTimeZoneById(sp.GetRequiredService<IOptions<DisplayOptions>>().Value.TimeZone));
        builder.Services.AddSingleton(TimeProvider.System);

        // Price persistence (Faemiyah.Repository-based PostgreSQL storage).
        builder.Services.AddSingleton<IConnectionStringAccessor, PgSqlConnectionStringAccessor>();
        builder.Services.AddSingleton<IDatabaseContextProvider<PriceDbContext>, PriceDbContextProvider>();
        builder.Services.AddSingleton<PricePointRepository>();
        builder.Services.AddSingleton<LivePriceState>();
        builder.Services.AddSingleton<PriceSeriesService>();

        builder.Services.AddPriceSource();
        builder.Services.AddSingleton<PriceIngestionService>();
        builder.Services.AddDataProtection().SetApplicationName("DayAheadPrice").PersistKeysToFileSystem(new DirectoryInfo(builder.Configuration["DataProtectionKeysPath"] ?? "/app/dpkeys/"));

        var app = builder.Build();

        // Configure the HTTP request pipeline.
        // The host reverse proxy reaches the container from a non-loopback gateway address, so trust any source. This is
        // safe only while the container port is published on the host's loopback interface alone.
        var forwardedHeadersOptions = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost
        };
        forwardedHeadersOptions.KnownIPNetworks.Clear();
        forwardedHeadersOptions.KnownProxies.Clear();
        app.UseForwardedHeaders(forwardedHeadersOptions);

        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
        }

        app.UseStaticFiles();
        app.UseRouting();
        app.UseAntiforgery();

        app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

        // Free, anonymous JSON summary of the latest available day's prices (min/max/time-weighted average). Fetches
        // newer data on access when stored prices are stale, exactly as the live page does.
        app.MapGet("/prices/summary", async (PriceSeriesService priceSeriesService, CancellationToken cancellationToken) =>
        {
            var summary = await priceSeriesService.GetLatestDaySummaryAsync(cancellationToken);

            return summary is null ? Results.NotFound() : Results.Ok(summary);
        });

        app.Run();
    }
}
