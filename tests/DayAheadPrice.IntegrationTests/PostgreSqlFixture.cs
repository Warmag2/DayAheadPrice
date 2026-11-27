using System.Threading.Tasks;
using NUnit.Framework;
using Testcontainers.PostgreSql;

namespace Faemiyah.DayAheadPrice.IntegrationTests;

/// <summary>
/// Starts one PostgreSQL container for the whole assembly. Tests isolate themselves by provisioning their own
/// database inside it.
/// </summary>
[SetUpFixture]
public class PostgreSqlFixture
{
    private const string PostgreSqlImage = "postgres:18-alpine";

    private static PostgreSqlContainer? _container;

    /// <summary>
    /// A connection string pointing at the container. The database it names is replaced per test.
    /// </summary>
    public static string BaseConnectionString { get; private set; } = string.Empty;

    [OneTimeSetUp]
    public async Task StartContainerAsync()
    {
        _container = new PostgreSqlBuilder(PostgreSqlImage).Build();
        await _container.StartAsync();
        BaseConnectionString = _container.GetConnectionString();
    }

    [OneTimeTearDown]
    public async Task StopContainerAsync()
    {
        if (_container != null)
        {
            await _container.DisposeAsync();
        }
    }
}
