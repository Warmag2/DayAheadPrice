using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Faemiyah.DayAheadPrice.Enums;
using Faemiyah.DayAheadPrice.Logic;
using NUnit.Framework;

namespace Faemiyah.DayAheadPrice.UnitTests;

[TestFixture]
public sealed class CalendarStepTests
{
    [TestCase(DstDay.FallBack, CalendarUnit.Quarter, 1, 100)]
    [TestCase(DstDay.SpringForward, CalendarUnit.Quarter, 1, 92)]
    [TestCase(DstDay.FallBack, CalendarUnit.Hour, 1, 25)]
    [TestCase(DstDay.SpringForward, CalendarUnit.Hour, 1, 23)]
    [TestCase(DstDay.FallBack, CalendarUnit.Hour, 4, 6)]
    [TestCase(DstDay.SpringForward, CalendarUnit.Hour, 4, 6)]
    [TestCase(DstDay.FallBack, CalendarUnit.Day, 1, 1)]
    [TestCase(DstDay.SpringForward, CalendarUnit.Day, 1, 1)]
    public void AddTo_WalkingADstDay_TakesTheLocalNumberOfStepsAndEndsAtMidnight(DstDay day, CalendarUnit unit, int count, int expectedSteps)
    {
        // Arrange
        var (startUtc, endUtc) = Dst.Bounds(day);
        var step = new CalendarStep(unit, count);

        // Act
        var visited = new List<DateTime> { startUtc };

        while (visited[^1] < endUtc)
        {
            visited.Add(step.AddTo(visited[^1], Dst.Helsinki));
        }

        // Assert
        visited.Should().HaveCount(expectedSteps + 1);
        visited[^1].Should().Be(endUtc);
    }

    [TestCase(DstDay.FallBack, CalendarUnit.Quarter, 1)]
    [TestCase(DstDay.SpringForward, CalendarUnit.Quarter, 1)]
    [TestCase(DstDay.FallBack, CalendarUnit.Hour, 1)]
    [TestCase(DstDay.SpringForward, CalendarUnit.Hour, 1)]
    [TestCase(DstDay.FallBack, CalendarUnit.Hour, 4)]
    [TestCase(DstDay.SpringForward, CalendarUnit.Hour, 4)]
    [TestCase(DstDay.FallBack, CalendarUnit.Day, 1)]
    [TestCase(DstDay.SpringForward, CalendarUnit.Day, 1)]
    public void SubtractFrom_WalkingBackOverADstDay_VisitsTheSameInstantsAsWalkingForward(DstDay day, CalendarUnit unit, int count)
    {
        // Arrange
        var (startUtc, endUtc) = Dst.Bounds(day);
        var step = new CalendarStep(unit, count);
        var forward = new List<DateTime>();

        for (var cursor = startUtc; cursor < endUtc; cursor = step.AddTo(cursor, Dst.Helsinki))
        {
            forward.Add(cursor);
        }

        // Act
        var backward = new List<DateTime>();

        for (var cursor = step.SubtractFrom(endUtc, Dst.Helsinki); cursor >= startUtc; cursor = step.SubtractFrom(cursor, Dst.Helsinki))
        {
            backward.Add(cursor);
        }

        // Assert
        backward.AsEnumerable().Reverse().Should().Equal(forward);
    }

    // The first 4-hour bar of the day ends at 04:00 local, so it is 5 hours long when the clocks go back and 3 when
    // they go forward.
    [TestCase(DstDay.FallBack, 2025, 10, 26, 2)]
    [TestCase(DstDay.SpringForward, 2026, 3, 29, 1)]
    public void AddTo_FourHourStepOverClockChange_StaysOnTheLocalGrid(DstDay day, int year, int month, int dayOfMonth, int expectedUtcHour)
    {
        // Arrange
        var (startUtc, _) = Dst.Bounds(day);
        var step = new CalendarStep(CalendarUnit.Hour, 4);

        // Act
        var next = step.AddTo(startUtc, Dst.Helsinki);

        // Assert
        next.Should().Be(Dst.Utc(year, month, dayOfMonth, expectedUtcHour));
    }

    [Test]
    public void AddTo_MonthContainingClockChange_EndsAtTheNextLocalMonthStart()
    {
        // Arrange
        var step = new CalendarStep(CalendarUnit.Month, 1);
        var octoberStartUtc = Dst.Utc(2025, 9, 30, 21);

        // Act
        var next = step.AddTo(octoberStartUtc, Dst.Helsinki);

        // Assert
        next.Should().Be(Dst.Utc(2025, 10, 31, 22));
    }

    [Test]
    public void SubtractFrom_MonthContainingClockChange_EndsAtThePreviousLocalMonthStart()
    {
        // Arrange
        var step = new CalendarStep(CalendarUnit.Month, 1);
        var novemberStartUtc = Dst.Utc(2025, 10, 31, 22);

        // Act
        var previous = step.SubtractFrom(novemberStartUtc, Dst.Helsinki);

        // Assert
        previous.Should().Be(Dst.Utc(2025, 9, 30, 21));
    }

    [Test]
    public void AddTo_NonPositiveCount_Throws()
    {
        // Arrange
        var step = new CalendarStep(CalendarUnit.Hour, 0);

        // Act
        var act = () => step.AddTo(Dst.Utc(2025, 1, 1), Dst.Helsinki);

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }
}
