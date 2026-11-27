using System;
using AwesomeAssertions;
using Faemiyah.DayAheadPrice.Entities;
using Faemiyah.DayAheadPrice.Logic;
using NUnit.Framework;

namespace Faemiyah.DayAheadPrice.UnitTests;

[TestFixture]
public sealed class PriceIngestionServiceTests
{
    private static readonly DateTime T0 = Dst.Utc(2025, 11, 3, 12);

    [Test]
    public void WithUnavailableSlots_SettledQuartersWithoutAPrice_AreMarkedUnavailable()
    {
        // Arrange
        var prices = new PriceList { Prices = { [T0] = 1m, [T0.AddMinutes(30)] = 3m } };

        // Act
        var slots = PriceIngestionService.WithUnavailableSlots(prices, T0, T0.AddHours(1), settledBeforeUtc: T0.AddDays(1));

        // Assert
        slots.Should().Equal(
            (T0, 1m),
            (T0.AddMinutes(15), null),
            (T0.AddMinutes(30), 3m),
            (T0.AddMinutes(45), null));
    }

    [Test]
    public void WithUnavailableSlots_UnsettledQuarters_AreNotMarked()
    {
        // Arrange: only the first quarter has ended by the settle instant.
        var prices = new PriceList();

        // Act
        var slots = PriceIngestionService.WithUnavailableSlots(prices, T0, T0.AddHours(1), settledBeforeUtc: T0.AddMinutes(20));

        // Assert
        slots.Should().Equal((T0, (decimal?)null));
    }

    [Test]
    public void WithUnavailableSlots_PricesOutsideTheRange_AreKept()
    {
        // Arrange: the API's whole-hour request returned a price before the requested start.
        var prices = new PriceList { Prices = { [T0] = 1m } };

        // Act
        var slots = PriceIngestionService.WithUnavailableSlots(prices, T0.AddMinutes(45), T0.AddHours(1), settledBeforeUtc: T0.AddDays(1));

        // Assert
        slots.Should().Equal((T0, 1m), (T0.AddMinutes(45), null));
    }
}
