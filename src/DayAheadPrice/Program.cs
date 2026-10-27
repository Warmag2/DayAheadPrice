using System.IO;
using DayAheadPrice.Components;
using DayAheadPrice.Database.Contexts;
using DayAheadPrice.Logic;
using DayAheadPrice.Options;
using DayAheadPrice.Repositories;
using Faemiyah.Common.Hosting;
using Faemiyah.Common.Hosting.Logging;
using Faemiyah.Repository.Interfaces;
using Faemiyah.Repository.Options;
using Faemiyah.Repository.Providers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DayAheadPrice;

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
        builder.Services.Configure<EndpointOptions>(builder.Configuration.GetSection("EndpointOptions"));
        builder.Services.Configure<PricingOptions>(builder.Configuration.GetSection("PricingOptions"));
        builder.Services.Configure<PersistenceOptions>(builder.Configuration.GetSection("PersistenceOptions"));
        builder.Services.Configure<SqlOptions>(builder.Configuration.GetSection("SqlOptions"));

        // Price persistence (Faemiyah.Repository-based PostgreSQL storage).
        builder.Services.AddSingleton<IConnectionStringAccessor, PgSqlConnectionStringAccessor>();
        builder.Services.AddSingleton<IDatabaseContextProvider<PriceDbContext>, PriceDbContextProvider>();
        builder.Services.AddSingleton<PricePointRepository>();
        builder.Services.AddSingleton<LivePriceState>();
        builder.Services.AddSingleton<PriceSeriesService>();

        builder.Services.AddSingleton<PriceContainer>();
        builder.Services.AddDataProtection().SetApplicationName("DayAheadPrice").PersistKeysToFileSystem(new DirectoryInfo(builder.Configuration["DataProtectionKeysPath"] ?? "/app/dpkeys/"));

        var app = builder.Build();

        // Configure the HTTP request pipeline.
        app.UseForwardedHeaders(new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
        });

        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
        }

        app.UseStaticFiles();
        app.UseRouting();
        app.UseAntiforgery();

        app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

        app.Run();
    }
}
