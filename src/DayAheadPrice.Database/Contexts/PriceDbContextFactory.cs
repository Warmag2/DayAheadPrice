using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace DayAheadPrice.Database.Contexts;

/// <summary>
/// This design time context factory is used by EF Core tooling to add/remove migrations without the
/// connection multiplexing provider that the running application uses.
/// </summary>
public class PriceDesignContextFactory : IDesignTimeDbContextFactory<PriceDbContext>
{
    /// <inheritdoc />
    public PriceDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddUserSecrets<PriceDesignContextFactory>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("Database");

        // Adding or removing migrations does not touch a database, so a placeholder is fine when none is set.
        if (string.IsNullOrWhiteSpace(connectionString))
        {
#pragma warning disable S2068 // Credentials should not be hard-coded
            connectionString = "Host=localhost;Database=dayaheadprice;Username=postgres;Password=postgres";
#pragma warning restore S2068 // Credentials should not be hard-coded
        }

        var optionsBuilder = new DbContextOptionsBuilder<PriceDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new PriceDbContext(optionsBuilder.Options);
    }
}
