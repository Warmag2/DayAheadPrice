using System;
using AwesomeAssertions;
using Faemiyah.DayAheadPrice.Entities;
using Faemiyah.DayAheadPrice.Enums;
using Faemiyah.DayAheadPrice.Extensions;
using Faemiyah.DayAheadPrice.Logic;
using NUnit.Framework;

namespace Faemiyah.DayAheadPrice.UnitTests;

[TestFixture]
public sealed class PriceNavigationTests
{
    private static readonly DateTime NowUtc = Dst.Utc(2026, 6, 1, 9, 20);

    [TestCase(DstDay.FallBack, 2025, 10, 24, 21)]
    [TestCase(DstDay.SpringForward, 2026, 3, 27, 22)]
    public void PanLeft_FinestLevelAtMidnight_MovesToThePreviousLocalMidnight(DstDay day, int year, int month, int dayOfMonth, int hour)
    {
        // Arrange
        var (startUtc, endUtc) = Dst.Bounds(day);
        var view = View(ZoomLevels.FinestIndex, startUtc, endUtc);

        // Act
        var state = PriceNavigation.PanLeft(view, Dst.Helsinki);

        // Assert
        state.Should().Be(new PriceViewState(ZoomLevels.FinestIndex, Dst.Utc(year, month, dayOfMonth, hour)));
    }

    [Test]
    public void PanLeft_FinestLevelMidDay_MovesToThatDaysMidnight()
    {
        // Arrange
        var (startUtc, _) = Dst.Bounds(DstDay.FallBack);
        var view = View(ZoomLevels.FinestIndex, Dst.Utc(2025, 10, 26, 6), Dst.Utc(2025, 10, 27, 6));

        // Act
        var state = PriceNavigation.PanLeft(view, Dst.Helsinki);

        // Assert
        state.Should().Be(new PriceViewState(ZoomLevels.FinestIndex, startUtc));
    }

    [TestCase(DstDay.FallBack)]
    [TestCase(DstDay.SpringForward)]
    public void PanRight_FinestLevelInThePast_MovesToTheNextLocalMidnight(DstDay day)
    {
        // Arrange
        var (startUtc, endUtc) = Dst.Bounds(day);
        var view = View(ZoomLevels.FinestIndex, startUtc, endUtc);

        // Act
        var state = PriceNavigation.PanRight(view, Dst.Helsinki, NowUtc);

        // Assert
        state.Should().Be(new PriceViewState(ZoomLevels.FinestIndex, endUtc));
    }

    [Test]
    public void PanRight_FinestLevelReachingThePresent_ReturnsToLive()
    {
        // Arrange
        var todayUtc = NowUtc.FloorLocal(CalendarUnit.Day, Dst.Helsinki);
        var view = View(ZoomLevels.FinestIndex, todayUtc, todayUtc.AddDays(1));

        // Act
        var state = PriceNavigation.PanRight(view, Dst.Helsinki, NowUtc);

        // Assert
        state.Should().Be(PriceViewState.Live);
    }

    [Test]
    public void PanRight_CoarsestLevel_MovesToTheNextLocalYear()
    {
        // Arrange
        var view = View(ZoomLevels.CoarsestIndex, Dst.Utc(2024, 12, 31, 22), Dst.Utc(2025, 12, 31, 22));

        // Act
        var state = PriceNavigation.PanRight(view, Dst.Helsinki, NowUtc);

        // Assert
        state.Should().Be(new PriceViewState(ZoomLevels.CoarsestIndex, Dst.Utc(2025, 12, 31, 22)));
    }

    [Test]
    public void ZoomIn_AtFinestLevel_KeepsTheLiveState()
    {
        // Arrange
        var view = View(ZoomLevels.FinestIndex, Dst.Utc(2025, 6, 1), Dst.Utc(2025, 6, 2), isLive: true);

        // Act
        var state = PriceNavigation.ZoomIn(view);

        // Assert
        state.Should().Be(PriceViewState.Live);
    }

    [Test]
    public void ZoomOut_AtCoarsestLevel_KeepsThePosition()
    {
        // Arrange
        var view = View(ZoomLevels.CoarsestIndex, Dst.Utc(2024, 12, 31, 22), Dst.Utc(2025, 12, 31, 22));

        // Act
        var state = PriceNavigation.ZoomOut(view);

        // Assert
        state.Should().Be(new PriceViewState(ZoomLevels.CoarsestIndex, Dst.Utc(2024, 12, 31, 22)));
    }

    [Test]
    public void ZoomOut_FromFinestLevel_KeepsTheWindowCentred()
    {
        // Arrange: the 25-hour fall-back day is centred 12.5 hours in, and the next level spans about 7 days.
        var (startUtc, endUtc) = Dst.Bounds(DstDay.FallBack);
        var view = View(ZoomLevels.FinestIndex, startUtc, endUtc);

        // Act
        var state = PriceNavigation.ZoomOut(view);

        // Assert
        state.Should().Be(new PriceViewState(ZoomLevels.FinestIndex + 1, startUtc.AddHours(12.5).AddDays(-3.5)));
    }

    private static PriceView View(int zoomIndex, DateTime fromUtc, DateTime toUtc, bool isLive = false) => new()
    {
        Resolution = ZoomLevels.All[zoomIndex].Resolution,
        HeaderUnit = ZoomLevels.All[zoomIndex].HeaderUnit,
        FromUtc = fromUtc,
        ToUtc = toUtc,
        ZoomIndex = zoomIndex,
        IsLive = isLive
    };
}
