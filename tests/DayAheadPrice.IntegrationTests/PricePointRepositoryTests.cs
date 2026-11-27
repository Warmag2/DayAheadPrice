using System;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Faemiyah.DayAheadPrice.Database.Repositories;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using static Faemiyah.DayAheadPrice.IntegrationTests.TestServices;

namespace Faemiyah.DayAheadPrice.IntegrationTests;

[TestFixture]
public sealed class PricePointRepositoryTests
{
    private static readonly DateTime T0 = Utc(2025, 6, 1, 12);

    private ServiceProvider _services = null!;
    private PricePointRepository _repository = null!;

    [SetUp]
    public void SetUp()
    {
        _services = Create();
        _repository = _services.GetRequiredService<PricePointRepository>();
    }

    [TearDown]
    public async Task TearDownAsync()
    {
        await _services.DisposeAsync();
    }

    [Test]
    public async Task GetBoundsAsync_NothingStored_ReturnsNull()
    {
        // Arrange: a fresh database.

        // Act
        var bounds = await _repository.GetBoundsAsync(Domain);

        // Assert
        bounds.Should().BeNull();
    }

    [Test]
    public async Task GetBoundsAsync_SlotsStored_ReturnsEarliestStartAndLatestEndOfTheDomainInUtc()
    {
        // Arrange
        await _repository.StoreAsync([Slot(T0, 1m), Slot(T0.AddHours(1), 2m), Slot(T0.AddDays(1), 3m, "OTHER")]);

        // Act
        var bounds = await _repository.GetBoundsAsync(Domain);

        // Assert
        bounds.Should().NotBeNull();
        bounds.Value.MinUtc.Should().Be(T0);
        bounds.Value.MaxUtc.Should().Be(T0.AddMinutes(75));
        bounds.Value.MinUtc.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Test]
    public async Task GetRangeAsync_PartlyOverlappingInterval_ReturnsOnlyOverlappingSlots()
    {
        // Arrange
        await _repository.StoreAsync([Slot(T0, 1m), Slot(T0.AddMinutes(15), 2m), Slot(T0.AddMinutes(30), 3m)]);

        // Act
        var slots = await _repository.GetRangeAsync(Domain, T0.AddMinutes(10), T0.AddMinutes(30));

        // Assert
        slots.Select(p => p.Price).Should().BeEquivalentTo([1m, 2m]);
    }
}
