using System;
using AwesomeAssertions;
using Faemiyah.DayAheadPrice.Enums;
using Faemiyah.DayAheadPrice.Extensions;
using NUnit.Framework;

namespace Faemiyah.DayAheadPrice.UnitTests;

[TestFixture]
public sealed class DateTimeExtensionsTests
{
    [TestCase(0, 0)]
    [TestCase(14, 0)]
    [TestCase(15, 15)]
    [TestCase(44, 30)]
    [TestCase(59, 45)]
    public void Floor_Quarter_FloorsToTheStartOfTheQuarterHour(int minute, int expectedMinute)
    {
        // Arrange
        var utc = Dst.Utc(2025, 10, 27, 10, minute).AddSeconds(30);

        // Act
        var floored = utc.Floor(CalendarUnit.Quarter);

        // Assert
        floored.Should().Be(Dst.Utc(2025, 10, 27, 10, expectedMinute));
    }

    [TestCase(0, 0, 10)]
    [TestCase(0, 1, 11)]
    [TestCase(59, 59, 11)]
    public void Ceiling_Hour_KeepsABoundaryAndRoundsAnythingElseUp(int minute, int second, int expectedHour)
    {
        // Arrange
        var utc = Dst.Utc(2025, 10, 27, 10, minute).AddSeconds(second);

        // Act
        var ceiled = utc.Ceiling();

        // Assert
        ceiled.Should().Be(Dst.Utc(2025, 10, 27, expectedHour));
    }

    [Test]
    public void Ceiling_MonthPastDecember_RollsOverToTheNextYear()
    {
        // Arrange
        var utc = Dst.Utc(2025, 12, 15);

        // Act
        var ceiled = utc.Ceiling(CalendarUnit.Month);

        // Assert
        ceiled.Should().Be(Dst.Utc(2026, 1, 1));
    }

    // 00:30Z and 01:30Z are both 03:30 on the Helsinki clock on 2025-10-26.
    [TestCase(0, 0)]
    [TestCase(1, 1)]
    public void FloorLocal_HourDuringRepeatedHour_FloorsEachOccurrenceSeparately(int utcHour, int expectedUtcHour)
    {
        // Arrange
        var utc = Dst.Utc(2025, 10, 26, utcHour, 30);

        // Act
        var floored = utc.FloorLocal(CalendarUnit.Hour, Dst.Helsinki);

        // Assert
        floored.Should().Be(Dst.Utc(2025, 10, 26, expectedUtcHour));
    }

    [TestCase(DstDay.FallBack)]
    [TestCase(DstDay.SpringForward)]
    public void FloorLocal_DayOnDstDay_FloorsToLocalMidnight(DstDay day)
    {
        // Arrange
        var (startUtc, endUtc) = Dst.Bounds(day);
        var lastTick = endUtc.AddTicks(-1);

        // Act
        var floored = lastTick.FloorLocal(CalendarUnit.Day, Dst.Helsinki);

        // Assert
        floored.Should().Be(startUtc);
    }

    [Test]
    public void FloorLocal_Month_FloorsToLocalMonthStart()
    {
        // Arrange
        var lastMinuteOfOctober = Dst.Utc(2025, 10, 31, 21, 59);

        // Act
        var floored = lastMinuteOfOctober.FloorLocal(CalendarUnit.Month, Dst.Helsinki);

        // Assert
        floored.Should().Be(Dst.Utc(2025, 9, 30, 21));
    }

    [TestCase(false, 0)]
    [TestCase(true, 1)]
    public void LocalToUtc_RepeatedTime_ResolvesToTheRequestedOccurrence(bool preferLater, int expectedUtcHour)
    {
        // Arrange
        var wall = Dst.Wall(2025, 10, 26, 3, 30);

        // Act
        var utc = wall.LocalToUtc(Dst.Helsinki, preferLater);

        // Assert
        utc.Should().Be(Dst.Utc(2025, 10, 26, expectedUtcHour, 30));
    }

    [Test]
    public void LocalToUtc_SkippedTime_LandsAfterTheGap()
    {
        // Arrange
        var skippedWall = Dst.Wall(2026, 3, 29, 3, 30);

        // Act
        var utc = skippedWall.LocalToUtc(Dst.Helsinki);

        // Assert
        utc.Should().Be(Dst.Utc(2026, 3, 29, 1, 30));
        utc.ToLocalWall(Dst.Helsinki).Should().Be(Dst.Wall(2026, 3, 29, 4, 30));
    }

    [Test]
    public void ToLocalWall_SummerInstant_ReturnsUnspecifiedWallTime()
    {
        // Arrange
        var utc = Dst.Utc(2025, 6, 1);

        // Act
        var wall = utc.ToLocalWall(Dst.Helsinki);

        // Assert
        wall.Should().Be(Dst.Wall(2025, 6, 1, 3));
        wall.Kind.Should().Be(DateTimeKind.Unspecified);
    }

    [Test]
    public void EnsureUtc_LocalKind_Throws()
    {
        // Arrange
        var local = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Local);

        // Act
        var act = () => local.EnsureUtc();

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void EnsureUtc_UnspecifiedKind_IsTreatedAsUtc()
    {
        // Arrange
        var unspecified = Dst.Wall(2025, 6, 1, 12);

        // Act
        var result = unspecified.EnsureUtc();

        // Assert
        result.Should().Be(Dst.Utc(2025, 6, 1, 12));
        result.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Test]
    public void DescribeSpan_WholeLocalMonth_ShowsYearAndMonth()
    {
        // Arrange
        var startUtc = Dst.Utc(2025, 9, 30, 21);
        var endUtc = Dst.Utc(2025, 10, 31, 22);

        // Act
        var description = DateTimeExtensions.DescribeSpan(startUtc, endUtc, Dst.Helsinki);

        // Assert
        description.Should().Be("2025-10");
    }

    [TestCase(DstDay.FallBack, "2025-10-26")]
    [TestCase(DstDay.SpringForward, "2026-03-29")]
    public void DescribeSpan_WholeDstDay_ShowsTheDate(DstDay day, string expected)
    {
        // Arrange
        var (startUtc, endUtc) = Dst.Bounds(day);

        // Act
        var description = DateTimeExtensions.DescribeSpan(startUtc, endUtc, Dst.Helsinki);

        // Assert
        description.Should().Be(expected);
    }

    [Test]
    public void DescribeSpan_SeveralDays_ShowsADateRange()
    {
        // Arrange
        var startUtc = Dst.Utc(2025, 10, 19, 21);
        var endUtc = Dst.Utc(2025, 10, 26, 22);

        // Act
        var description = DateTimeExtensions.DescribeSpan(startUtc, endUtc, Dst.Helsinki);

        // Assert
        description.Should().Be("10-20 \u2014 10-27");
    }

    [Test]
    [SetCulture("fi-FI")]
    public void DescribeSpan_WithinADay_ShowsTheLocalTimeRange()
    {
        // Arrange
        var startUtc = Dst.Utc(2025, 10, 27, 10);
        var endUtc = Dst.Utc(2025, 10, 27, 11);

        // Act
        var description = DateTimeExtensions.DescribeSpan(startUtc, endUtc, Dst.Helsinki);

        // Assert
        description.Should().Be("10-27 \u23F2 12:00 \u2014 13:00");
    }
}
