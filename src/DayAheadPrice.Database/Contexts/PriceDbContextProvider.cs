using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RepositoryPrototype.Interfaces;
using RepositoryPrototype.Options;
using RepositoryPrototype.Providers;

namespace DayAheadPrice.Database.Contexts;

/// <summary>
/// Provides <see cref="PriceDbContext"/> instances, creating and migrating the database on first use.
/// </summary>
public class PriceDbContextProvider : PgSqlDatabaseContextProvider<PriceDbContext>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PriceDbContextProvider"/> class.
    /// </summary>
    /// <param name="loggerFactory">The logger factory.</param>
    /// <param name="sqlOptions">The database options.</param>
    /// <param name="connectionStringAccessor">The connection string accessor.</param>
    public PriceDbContextProvider(
        ILoggerFactory loggerFactory,
        IOptions<SqlOptions> sqlOptions,
        IConnectionStringAccessor connectionStringAccessor)
        : base(loggerFactory.CreateLogger<PgSqlDatabaseContextProvider<PriceDbContext>>(), sqlOptions, connectionStringAccessor)
    {
    }

    /// <inheritdoc />
    protected override PriceDbContext GetContextInstance(DbContextOptions<PriceDbContext> dbContextOptions)
    {
        return new PriceDbContext(dbContextOptions);
    }

    /// <inheritdoc />
    protected override async Task OnDatabaseProvisionedAsync(
        PriceDbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        await dbContext.Database.MigrateAsync(cancellationToken);
    }
}
