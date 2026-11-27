using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Faemiyah.DayAheadPrice.Database.Repositories;
using Faemiyah.DayAheadPrice.Extensions;
using Faemiyah.DayAheadPrice.Logic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using static Faemiyah.DayAheadPrice.IntegrationTests.TestServices;

namespace Faemiyah.DayAheadPrice.IntegrationTests;

[TestFixture]
public sealed class PriceIngestionServiceTests
{
    private ServiceProvider _services = null!;
    private PriceIngestionService _ingestion = null!;
    private PricePointRepository _repository = null!;

    [SetUp]
    public void SetUp()
    {
        _services = Create(generateTestData: true);
        _ingestion = _services.GetRequiredService<PriceIngestionService>();
        _repository = _services.GetRequiredService<PricePointRepository>();
    }

    [TearDown]
    public async Task TearDownAsync()
    {
        await _services.DisposeAsync();
    }

    [Test]
    public async Task FillGapsAsync_PastGap_PersistsEveryQuarterOfIt()
    {
        // Arrange
        (DateTime, DateTime)[] gaps = [(FallBackDayStartUtc, FallBackDayEndUtc)];

        // Act
        var changed = await _ingestion.FillGapsAsync(gaps);

        // Assert
        changed.Should().BeTrue();
        (await _repository.GetRangeAsync(Domain, FallBackDayStartUtc, FallBackDayEndUtc)).Should().HaveCount(100);
    }

    [Test]
    public async Task FillGapsAsync_GapAttemptedWithinTheCooldown_IsNotFetchedAgain()
    {
        // Arrange
        (DateTime, DateTime)[] gaps = [(FallBackDayStartUtc, FallBackDayEndUtc)];
        await _ingestion.FillGapsAsync(gaps);

        // Act
        var changed = await _ingestion.FillGapsAsync(gaps);

        // Assert
        changed.Should().BeFalse();
    }

    [Test]
    public async Task FillGapsAsync_FutureGap_IsLeftToTheFreshnessPath()
    {
        // Arrange
        var tomorrowUtc = NowUtc.AddDays(1);
        (DateTime, DateTime)[] gaps = [(tomorrowUtc, tomorrowUtc.AddHours(1))];

        // Act
        var changed = await _ingestion.FillGapsAsync(gaps);

        // Assert
        changed.Should().BeFalse();
        (await _repository.GetBoundsAsync(Domain)).Should().BeNull();
    }

    [Test]
    public async Task FillGapsAsync_GapWhoseEarlierFillWasCancelled_IsFetched()
    {
        // Arrange
        (DateTime, DateTime)[] gaps = [(FallBackDayStartUtc, FallBackDayEndUtc)];
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var cancelledFill = () => _ingestion.FillGapsAsync(gaps, cancelled.Token);
        await cancelledFill.Should().ThrowAsync<OperationCanceledException>();

        // Act
        var changed = await _ingestion.FillGapsAsync(gaps);

        // Assert
        changed.Should().BeTrue();
    }

    [Test]
    public async Task FillGapsAsync_GapInsideAnEarlierGapWithDifferentEdges_IsNotFetchedAgain()
    {
        // Arrange: chunks are cut at UTC month boundaries, so the earlier gap reserved all of December.
        await _ingestion.FillGapsAsync([(Utc(2025, 11, 20), Utc(2026, 1, 10))]);

        // Act
        var changed = await _ingestion.FillGapsAsync([(Utc(2025, 12, 1), Utc(2025, 12, 31))]);

        // Assert
        changed.Should().BeFalse();
    }

    [Test]
    public async Task FillGapsAsync_FetchReturningOnlyUnchangedStoredSlots_WritesNothing()
    {
        // Arrange: requests are widened to whole hours, so the first fill already stored the whole hour.
        await _ingestion.FillGapsAsync([(FallBackDayStartUtc.AddMinutes(15), FallBackDayStartUtc.AddHours(1))]);
        var liveState = _services.GetRequiredService<LivePriceState>();
        var version = liveState.Version;

        // Act
        var changed = await _ingestion.FillGapsAsync([(FallBackDayStartUtc, FallBackDayStartUtc.AddMinutes(15))]);

        // Assert
        changed.Should().BeFalse();
        liveState.Version.Should().Be(version);
        (await _repository.GetRangeAsync(Domain, FallBackDayStartUtc, FallBackDayStartUtc.AddHours(1))).Should().HaveCount(4);
    }

    [Test]
    public async Task FillGapsAsync_FetchReturningOtherPricesForStoredSlots_UpdatesThemInPlace()
    {
        // Arrange
        var endUtc = FallBackDayStartUtc.AddHours(1);
        await _services.SeedQuartersAsync(FallBackDayStartUtc, endUtc, _ => 99m);

        // Act
        var changed = await _ingestion.FillGapsAsync([(FallBackDayStartUtc, endUtc)]);

        // Assert
        changed.Should().BeTrue();
        var stored = await _repository.GetRangeAsync(Domain, FallBackDayStartUtc, endUtc);
        stored.Should().HaveCount(4);
        stored.Should().NotContain(p => p.Price == 99m);
    }

    [Test]
    public async Task FillGapsAsync_GapReachingBeforeTheHistoryStart_IsFetchedFromTheHistoryStartOnly()
    {
        // Arrange
        (DateTime, DateTime)[] gaps = [(Utc(2025, 9, 1), Utc(2025, 10, 2))];

        // Act
        await _ingestion.FillGapsAsync(gaps);

        // Assert
        (await _repository.GetBoundsAsync(Domain)).Should().Be((Market.HistoryStartUtc, Utc(2025, 10, 2)));
    }

    [Test]
    public async Task EnsureRangeAsync_EmptyDatabase_StoresTheRequestedRangeAndReturnsItsBounds()
    {
        // Arrange: a fresh database.

        // Act
        var bounds = await _ingestion.EnsureRangeAsync(FallBackDayStartUtc, FallBackDayEndUtc);

        // Assert
        bounds.Should().Be((FallBackDayStartUtc, FallBackDayEndUtc));
        (await _repository.GetBoundsAsync(Domain)).Should().Be(bounds);
    }

    [Test]
    public async Task EnsureRangeAsync_RangeAlreadyStored_ReturnsTheStoredBounds()
    {
        // Arrange
        await _services.SeedQuartersAsync(FallBackDayStartUtc.AddDays(-1), FallBackDayEndUtc, _ => 1m);

        // Act
        var bounds = await _ingestion.EnsureRangeAsync(FallBackDayStartUtc, FallBackDayEndUtc);

        // Assert
        bounds.Should().Be((FallBackDayStartUtc.AddDays(-1), FallBackDayEndUtc));
    }

    [Test]
    public async Task EnsureRangeAsync_RangeFarBeforeStoredData_StopsAtTheRangeEnd()
    {
        // Arrange
        await _services.SeedQuartersAsync(Utc(2026, 1, 1), Utc(2026, 1, 2), _ => 1m);

        // Act
        await _ingestion.EnsureRangeAsync(FallBackDayStartUtc, FallBackDayEndUtc);

        // Assert
        (await _repository.GetRangeAsync(Domain, FallBackDayEndUtc, Utc(2026, 1, 1))).Should().BeEmpty();
    }

    [Test]
    public async Task EnsureFreshAsync_EmptyDatabase_StoresPricesReachingIntoTomorrow()
    {
        // Arrange: a fresh database.

        // Act
        await _ingestion.EnsureFreshAsync();

        // Assert
        var bounds = await _repository.GetBoundsAsync(Domain);
        bounds.Should().NotBeNull();
        bounds.Value.MaxUtc.Should().BeAfter(NowUtc.AddHours(LivePriceState.LookaheadHours));
    }

    [Test]
    public async Task EnsureFreshAsync_StoredDataReachingPastTheLookahead_FetchesNothing()
    {
        // Arrange
        var storedToUtc = NowUtc.Floor().AddHours(LivePriceState.LookaheadHours + 1);
        await _services.SeedQuartersAsync(NowUtc.Floor().AddDays(-1), storedToUtc, _ => 1m);

        // Act
        await _ingestion.EnsureFreshAsync();

        // Assert
        (await _repository.GetBoundsAsync(Domain)).Should().Be((NowUtc.Floor().AddDays(-1), storedToUtc));
    }

    [Test]
    public async Task EnsureFreshAsync_ClockPassingTheLookaheadInALaterHour_FetchesNewPrices()
    {
        // Arrange
        var storedToUtc = NowUtc.Floor().AddHours(LivePriceState.LookaheadHours + 1);
        await _services.SeedQuartersAsync(NowUtc.Floor().AddDays(-1), storedToUtc, _ => 1m);
        await _ingestion.EnsureFreshAsync();
        _services.GetRequiredService<FakeTimeProvider>().Advance(TimeSpan.FromHours(1));

        // Act
        await _ingestion.EnsureFreshAsync();

        // Assert
        var bounds = await _repository.GetBoundsAsync(Domain);
        bounds.Should().NotBeNull();
        bounds.Value.MaxUtc.Should().BeAfter(storedToUtc);
    }

    [Test]
    public async Task EnsureFreshAsync_StoredDataEndingLongAgo_FetchesOnlyTheLastDay()
    {
        // Arrange
        var storedToUtc = NowUtc.Floor().AddDays(-60);
        await _services.SeedQuartersAsync(storedToUtc.AddDays(-1), storedToUtc, _ => 1m);

        // Act
        await _ingestion.EnsureFreshAsync();

        // Assert: the hole before the last day is left to the history gap fill.
        (await _repository.GetRangeAsync(Domain, storedToUtc, NowUtc.AddDays(-1).Floor())).Should().BeEmpty();
        var bounds = await _repository.GetBoundsAsync(Domain);
        bounds.Should().NotBeNull();
        bounds.Value.MaxUtc.Should().BeAfter(NowUtc.AddHours(LivePriceState.LookaheadHours));
    }
}
