using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Faemiyah.DayAheadPrice.Database.Repositories;
using Faemiyah.DayAheadPrice.Entities;
using Faemiyah.DayAheadPrice.Extensions;
using Faemiyah.DayAheadPrice.Logic;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using static Faemiyah.DayAheadPrice.IntegrationTests.TestServices;

namespace Faemiyah.DayAheadPrice.IntegrationTests;

[TestFixture]
public sealed class PriceSeriesServiceTests
{

    private ServiceProvider _services = null!;
    private PriceSeriesService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _services = Create();
        _service = _services.GetRequiredService<PriceSeriesService>();
    }

    [TearDown]
    public async Task TearDownAsync()
    {
        await _services.DisposeAsync();
    }

    [Test]
    public async Task RenderAsync_FinestLevelAnchoredOnFallBackDay_RendersAllHundredQuarters()
    {
        // Arrange: store two days either side, so the history and gap checks find nothing to fetch.
        await _services.SeedQuartersAsync(FallBackDayStartUtc.AddDays(-2), FallBackDayEndUtc.AddDays(2), _ => 5m);
        var state = new PriceViewState(ZoomLevels.FinestIndex, Utc(2025, 10, 26, 12));

        // Act
        var view = await _service.RenderAsync(state);

        // Assert
        view.FromUtc.Should().Be(FallBackDayStartUtc);
        view.ToUtc.Should().Be(FallBackDayEndUtc);
        view.Bars.Should().HaveCount(100);
        view.Bars.Values.Should().AllSatisfy(v => v.Should().Be(5m));
        view.CanPanLeft.Should().BeTrue();
        view.CanPanRight.Should().BeTrue();
    }

    [Test]
    public async Task RenderAsync_AnchorAtTheStartOfTime_ShowsTheEarliestStoredWindow()
    {
        // Arrange
        await _services.SeedQuartersAsync(Utc(2025, 11, 1), Utc(2025, 11, 3), _ => 5m);
        var state = new PriceViewState(ZoomLevels.CoarsestIndex, DateTime.MinValue);

        // Act
        var view = await _service.RenderAsync(state);

        // Assert: the coarsest window is the local year 2025.
        view.FromUtc.Should().Be(Utc(2024, 12, 31, 22));
        view.Bars.Keys.Should().Equal(Utc(2025, 10, 31, 22));
        view.CanPanLeft.Should().BeFalse();
    }

    [Test]
    public async Task RenderAsync_AnchorAtTheEndOfTime_ShowsTheLatestStoredWindow()
    {
        // Arrange
        await _services.SeedQuartersAsync(Utc(2025, 11, 1), Utc(2025, 11, 3), _ => 5m);
        var state = new PriceViewState(ZoomLevels.CoarsestIndex, DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc));

        // Act
        var view = await _service.RenderAsync(state);

        // Assert
        view.FromUtc.Should().Be(Utc(2024, 12, 31, 22));
        view.CanPanRight.Should().BeFalse();
    }

    [Test]
    public async Task GetLatestDaySummaryAsync_FallBackDayStored_SummarisesThe25HourLocalDay()
    {
        // Arrange: a flat day with one expensive quarter at 18:00 local (16:00Z, after the clocks went back).
        var peakUtc = Utc(2025, 10, 26, 16);
        await _services.SeedQuartersAsync(FallBackDayStartUtc, FallBackDayEndUtc, t => t == peakUtc ? 8m : 4m);

        // Act
        var summary = await _service.GetLatestDaySummaryAsync();

        // Assert
        summary.Should().NotBeNull();
        summary.Date.Should().Be(new DateOnly(2025, 10, 26));
        summary.Min.Should().Be(4m);
        summary.Max.Should().Be(8m);
        summary.MaxTime.Should().Be("18:00");
        summary.Average.Should().Be(4.04m);
    }

    [TestCase(10, "High average price.", "High average price during hour 18:00.")]
    [TestCase(11, "High average price during hour 18:00.")]
    public async Task GetLatestDaySummaryAsync_ConfiguredThresholds_RaiseTheExceededAlerts(int averageThreshold, params string[] expectedAlerts)
    {
        // Arrange: a day averaging 10.2, with one hour (18:00 local) at 15 and no quarter above 15.
        var peakHourUtc = Utc(2025, 10, 26, 16);
        await using var services = Create(settings: new Dictionary<string, string?>
        {
            ["PricingOptions:AlertAverageThreshold"] = averageThreshold.ToString(CultureInfo.InvariantCulture),
            ["PricingOptions:AlertHourThreshold"] = "14",
            ["PricingOptions:AlertMaxThreshold"] = "15"
        });
        await services.SeedQuartersAsync(FallBackDayStartUtc, FallBackDayEndUtc, t => t >= peakHourUtc && t < peakHourUtc.AddHours(1) ? 15m : 10m);

        // Act
        var summary = await services.GetRequiredService<PriceSeriesService>().GetLatestDaySummaryAsync();

        // Assert
        summary.Should().NotBeNull();
        summary.Alerts.Should().Equal(expectedAlerts);
    }

    [Test]
    public async Task RenderAsync_UnavailableSlotsStored_RendersThemAsBarsWithoutAValue()
    {
        // Arrange: the 12:00 local hour (10:00Z) of the fall-back day is unavailable.
        var unavailableFromUtc = Utc(2025, 10, 26, 10);
        await _services.SeedQuartersAsync(
            FallBackDayStartUtc.AddDays(-2),
            FallBackDayEndUtc.AddDays(2),
            t => t >= unavailableFromUtc && t < unavailableFromUtc.AddHours(1) ? null : 5m);

        // Act
        var view = await _service.RenderAsync(new PriceViewState(ZoomLevels.FinestIndex, Utc(2025, 10, 26, 12)));

        // Assert
        view.Bars.Should().HaveCount(100);
        view.Bars.Where(b => b.Value == null).Select(b => b.Key).Should().Equal(
            unavailableFromUtc, unavailableFromUtc.AddMinutes(15), unavailableFromUtc.AddMinutes(30), unavailableFromUtc.AddMinutes(45));
    }

    [Test]
    public async Task GetLatestDaySummaryAsync_DayPartlyUnavailable_SummarisesThePricedSlotsOnly()
    {
        // Arrange: the first 50 quarters are unavailable; the rest are flat with one expensive quarter at 18:00 local.
        var peakUtc = Utc(2025, 10, 26, 16);
        var pricedFromUtc = FallBackDayStartUtc.AddMinutes(15 * 50);
        await _services.SeedQuartersAsync(FallBackDayStartUtc, pricedFromUtc, _ => null);
        await _services.SeedQuartersAsync(pricedFromUtc, FallBackDayEndUtc, t => t == peakUtc ? 8m : 4m);

        // Act
        var summary = await _service.GetLatestDaySummaryAsync();

        // Assert
        summary.Should().NotBeNull();
        summary.Min.Should().Be(4m);
        summary.Max.Should().Be(8m);
        summary.MaxTime.Should().Be("18:00");
        summary.Average.Should().Be(4.08m);
    }

    [TestCase(6, true)]
    [TestCase(7, false)]
    public async Task GetLatestDaySummaryAsync_NewestDaysUnavailable_LooksBackAtMostSevenDays(int unavailableDays, bool expectSummary)
    {
        // Arrange: the Helsinki day 2025-11-10 is priced; the days after it are unavailable.
        var pricedFromUtc = Utc(2025, 11, 9, 22);
        var pricedToUtc = pricedFromUtc.AddDays(1);
        await _services.SeedQuartersAsync(pricedFromUtc, pricedToUtc.AddDays(unavailableDays), t => t < pricedToUtc ? 4m : null);

        // Act
        var summary = await _service.GetLatestDaySummaryAsync();

        // Assert
        if (expectSummary)
        {
            summary.Should().NotBeNull();
            summary.Date.Should().Be(new DateOnly(2025, 11, 10));
        }
        else
        {
            summary.Should().BeNull();
        }
    }

    [Test]
    public async Task GetLatestDaySummaryAsync_AfterAnUnannouncedDatabaseWrite_IsServedFromTheCache()
    {
        // Arrange: the summary is built once; then the peak changes behind the app's back.
        var peakUtc = Utc(2025, 10, 26, 16);
        await _services.SeedQuartersAsync(FallBackDayStartUtc, FallBackDayEndUtc, t => t == peakUtc ? 8m : 4m);
        await _service.GetLatestDaySummaryAsync();
        await _services.GetRequiredService<PricePointRepository>().StoreAsync([Slot(peakUtc, 9m)]);

        // Act
        var summary = await _service.GetLatestDaySummaryAsync();

        // Assert
        summary.Should().NotBeNull();
        summary.Max.Should().Be(8m);
    }

    [Test]
    public async Task GetLatestDaySummaryAsync_AfterTheAppWritesData_IsRebuilt()
    {
        // Arrange: the summary is built once; then the peak changes through a write the app announces.
        var peakUtc = Utc(2025, 10, 26, 16);
        await _services.SeedQuartersAsync(FallBackDayStartUtc, FallBackDayEndUtc, t => t == peakUtc ? 8m : 4m);
        await _service.GetLatestDaySummaryAsync();
        await _services.GetRequiredService<PricePointRepository>().StoreAsync([Slot(peakUtc, 9m)]);
        _services.GetRequiredService<LivePriceState>().Invalidate();

        // Act
        var summary = await _service.GetLatestDaySummaryAsync();

        // Assert
        summary.Should().NotBeNull();
        summary.Max.Should().Be(9m);
    }

    [Test]
    public async Task RenderAsync_LiveViewAfterAnUnannouncedDatabaseWrite_IsServedFromTheSnapshot()
    {
        // Arrange: the live view is loaded once; then a price changes behind the app's back.
        var hourUtc = NowUtc.Floor();
        var changedUtc = hourUtc.AddHours(-3);
        await _services.SeedQuartersAsync(hourUtc.AddHours(-12), hourUtc.AddHours(2), _ => 5m);
        var live = new PriceViewState(ZoomLevels.FinestIndex, null);
        await _service.RenderAsync(live);
        await _services.GetRequiredService<PricePointRepository>().StoreAsync([Slot(changedUtc, 7m)]);

        // Act
        var view = await _service.RenderAsync(live);

        // Assert: the stale (but complete) snapshot is served without touching the database.
        view.Bars[changedUtc].Should().Be(5m);
    }

    [Test]
    public async Task RenderAsync_LiveViewAfterAGapIsFilled_ShowsTheFilledSlots()
    {
        // Arrange: the live view is loaded with an hour missing; then the app fills it.
        await using var services = Create(generateTestData: true);
        var service = services.GetRequiredService<PriceSeriesService>();
        var hourUtc = NowUtc.Floor();
        var gapFromUtc = hourUtc.AddHours(-3);
        var gapToUtc = hourUtc.AddHours(-2);
        await services.SeedQuartersAsync(hourUtc.AddHours(-12), gapFromUtc, _ => 5m);
        await services.SeedQuartersAsync(gapToUtc, hourUtc.AddHours(2), _ => 5m);
        var live = new PriceViewState(ZoomLevels.FinestIndex, null);
        (await service.RenderAsync(live)).Bars.Should().NotContainKey(gapFromUtc);
        await services.GetRequiredService<PriceIngestionService>().FillGapsAsync([(gapFromUtc, gapToUtc)]);

        // Act
        var view = await service.RenderAsync(live);

        // Assert
        view.Bars.Should().ContainKey(gapFromUtc);
    }
}
