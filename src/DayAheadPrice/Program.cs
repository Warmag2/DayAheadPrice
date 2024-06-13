using DayAheadPrice.Components;
using DayAheadPrice.Logic;
using Microsoft.AspNetCore.DataProtection;

namespace DayAheadPrice;

/// <summary>
/// Main program class.
/// </summary>
public static class Program
{
    /// <summary>
    /// Main program entrypoint.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add services to the container.
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();

        // Custom services
        builder.Services.Configure<EndpointOptions>(builder.Configuration.GetSection("EndpointOptions"));
        builder.Services.AddSingleton<PriceContainer>();
        builder.Services.AddDataProtection().SetApplicationName("DayAheadPrice").PersistKeysToFileSystem(new DirectoryInfo(@"/app/dpkeys/"));

        var app = builder.Build();

        // Configure the HTTP request pipeline.
        /*if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
        }

        app.UseStaticFiles();
        app.UseRouting();
        app.UseAntiforgery();

        app.UseHsts();
        app.UseHttpsRedirection();*/

        // Configure the HTTP request pipeline.
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
