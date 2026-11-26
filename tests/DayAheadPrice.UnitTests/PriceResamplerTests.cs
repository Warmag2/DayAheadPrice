using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Faemiyah.DayAheadPrice.Database.Entities;
using Faemiyah.DayAheadPrice.Enums;
using Faemiyah.DayAheadPrice.Logic;
using NUnit.Framework;

namespace Faemiyah.DayAheadPrice.UnitTests;

[TestFixture]
public sealed class PriceResamplerTests
{
    private static readonly DateTime T0 = Dst.Utc(2025, 6, 1, 12);
    private static readonly CalendarStep Hourly = new(CalendarUnit.Hour, 1);

    [Test]
    public void ComputeBar_PartlyCoveredBar_AveragesOverTheCoveredTimeOnly()
    {
        // Arrange
        var intervals = new List<(DateTime, DateTime, decimal?)>
        {
            (T0, T0.AddMinutes(15), 10m),
            (T0.AddMinutes(15), T0.AddMinutes(30), 20m)
        };

        // Act
        var (value, coveredTicks, pricedTicks) = PriceResampler.ComputeBar(intervals, T0, T0.AddHours(1));

        // Assert
        value.Should().Be(15m);
        coveredTicks.Should().Be(TimeSpan.FromMinutes(30).Ticks);
        pricedTicks.Should().Be(TimeSpan.FromMinutes(30).Ticks);
    }

    [Test]
    public void ComputeBar_IntervalsStraddlingTheBarEdges_CountOnlyTheOverlap()
    {
        // Arrange: 15 minutes at 10 and 45 minutes at 40 fall inside the bar.
        var intervals = new List<(DateTime, DateTime, decimal?)>
        {
            (T0.AddMinutes(-45), T0.AddMinutes(15), 10m),
            (T0.AddMinutes(15), T0.AddMinutes(75), 40m)
        };

        // Act
        var (value, coveredTicks, _) = PriceResampler.ComputeBar(intervals, T0, T0.AddHours(1));

        // Assert
        value.Should().Be(32.5m);
        coveredTicks.Should().Be(TimeSpan.FromHours(1).Ticks);
    }

    [Test]
    public void ComputeBar_NoOverlap_ReturnsZeroTicks()
    {
        // Arrange
        var intervals = new List<(DateTime, DateTime, decimal?)> { (T0.AddHours(-1), T0, 10m) };

        // Act
        var bar = PriceResampler.ComputeBar(intervals, T0, T0.AddHours(1));

        // Assert
        bar.Should().Be(((decimal?)null, 0L, 0L));
    }

    [Test]
    public void ComputeBar_SomeUnavailableSlots_CoverTheBarButAreLeftOutOfTheAverage()
    {
        // Arrange
        var intervals = new List<(DateTime, DateTime, decimal?)>
        {
            (T0, T0.AddMinutes(15), 10m),
            (T0.AddMinutes(15), T0.AddMinutes(30), null),
            (T0.AddMinutes(30), T0.AddMinutes(45), 20m),
            (T0.AddMinutes(45), T0.AddHours(1), null)
        };

        // Act
        var (value, coveredTicks, pricedTicks) = PriceResampler.ComputeBar(intervals, T0, T0.AddHours(1));

        // Assert
        value.Should().Be(15m);
        coveredTicks.Should().Be(TimeSpan.FromHours(1).Ticks);
        pricedTicks.Should().Be(TimeSpan.FromMinutes(30).Ticks);
    }

    [Test]
    public void ComputeBar_OnlyUnavailableSlots_HasNoValueButIsCovered()
    {
        // Arrange
        var intervals = new List<(DateTime, DateTime, decimal?)> { (T0, T0.AddHours(1), null) };

        // Act
        var bar = PriceResampler.ComputeBar(intervals, T0, T0.AddHours(1));

        // Assert
        bar.Should().Be(((decimal?)null, TimeSpan.FromHours(1).Ticks, 0L));
    }

    [Test]
    public void ComputeGaps_SparseSlots_ReturnsEachUncoveredRange()
    {
        // Arrange
        var slots = new[] { Slot(T0, 15), Slot(T0.AddMinutes(30), 15) };

        // Act
        var gaps = PriceResampler.ComputeGaps(slots, T0, T0.AddHours(1));

        // Assert
        gaps.Should().Equal((T0.AddMinutes(15), T0.AddMinutes(30)), (T0.AddMinutes(45), T0.AddHours(1)));
    }

    [Test]
    public void ComputeGaps_NoSlots_ReturnsTheWholeWindow()
    {
        // Arrange
        var slots = Array.Empty<PricePoint>();

        // Act
        var gaps = PriceResampler.ComputeGaps(slots, T0, T0.AddHours(1));

        // Assert
        gaps.Should().Equal((T0, T0.AddHours(1)));
    }

    [Test]
    public void ComputeGaps_SlotsCoveringBeyondTheWindow_ReturnsNoGaps()
    {
        // Arrange
        var slots = new[] { Slot(T0.AddMinutes(-15), 45), Slot(T0.AddMinutes(30), 60) };

        // Act
        var gaps = PriceResampler.ComputeGaps(slots, T0, T0.AddHours(1));

        // Assert
        gaps.Should().BeEmpty();
    }

    [TestCase(DstDay.FallBack, 25)]
    [TestCase(DstDay.SpringForward, 23)]
    public void ResampleIntervals_HourlyBarsOnDstDay_ProduceOneBarPerLocalHour(DstDay day, int expectedHours)
    {
        // Arrange: each quarter is priced by the real hour it falls in, so every bar averages to its own index.
        var (startUtc, endUtc) = Dst.Bounds(day);
        var intervals = Enumerable.Range(0, (int)((endUtc - startUtc).TotalMinutes / 15))
            .Select(i => (startUtc.AddMinutes(15 * i), startUtc.AddMinutes(15 * (i + 1)), (decimal?)(i / 4)))
            .ToList();

        // Act
        var bars = PriceResampler.ResampleIntervals(intervals, startUtc, endUtc, Hourly, Dst.Helsinki);

        // Assert
        bars.Keys.Should().Equal(Enumerable.Range(0, expectedHours).Select(h => startUtc.AddHours(h)));
        bars.Values.Should().Equal(Enumerable.Range(0, expectedHours).Select(h => (decimal?)h));
    }

    [Test]
    public void ResampleIntervals_BarsWithoutData_AreLeftOut()
    {
        // Arrange
        var intervals = new List<(DateTime, DateTime, decimal?)> { (T0.AddHours(1), T0.AddHours(2), 5m) };

        // Act
        var bars = PriceResampler.ResampleIntervals(intervals, T0, T0.AddHours(3), Hourly, Dst.Helsinki);

        // Assert
        bars.Keys.Should().Equal(T0.AddHours(1));
    }

    [Test]
    public void ResampleIntervals_BarOfOnlyUnavailableSlots_IsKeptWithoutAValue()
    {
        // Arrange
        var intervals = new List<(DateTime, DateTime, decimal?)>
        {
            (T0, T0.AddHours(1), 5m),
            (T0.AddHours(1), T0.AddHours(2), null)
        };

        // Act
        var bars = PriceResampler.ResampleIntervals(intervals, T0, T0.AddHours(2), Hourly, Dst.Helsinki);

        // Assert
        bars.Keys.Should().Equal(T0, T0.AddHours(1));
        bars.Values.Should().Equal(5m, null);
    }

    private static PricePoint Slot(DateTime startUtc, int minutes) => new()
    {
        Domain = "TEST",
        Price = 1m,
        DateBegin = startUtc,
        DateEnd = startUtc.AddMinutes(minutes)
    };
}
