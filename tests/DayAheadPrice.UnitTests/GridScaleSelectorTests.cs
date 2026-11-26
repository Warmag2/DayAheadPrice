using System;
using AwesomeAssertions;
using Faemiyah.DayAheadPrice.Logic;
using NUnit.Framework;

namespace Faemiyah.DayAheadPrice.UnitTests;

[TestFixture]
public sealed class GridScaleSelectorTests
{
    [Test]
    public void GetAxis_PositivePrices_SpansFromZeroToTheNextGridStep()
    {
        // Arrange
        decimal?[] prices = [3.2m, 7.9m];

        // Act
        var axis = GridScaleSelector.GetAxis(prices);

        // Assert
        axis.Should().Be((8m, 0m));
    }

    [Test]
    public void GetAxis_NegativePrices_SpansUpToZero()
    {
        // Arrange
        decimal?[] prices = [-3.5m, -1m];

        // Act
        var axis = GridScaleSelector.GetAxis(prices);

        // Assert
        axis.Should().Be((0m, -3.5m));
    }

    [Test]
    public void GetAxis_AllPricesZero_SpansZeroToOne()
    {
        // Arrange
        decimal?[] prices = [0m, 0m];

        // Act
        var axis = GridScaleSelector.GetAxis(prices);

        // Assert
        axis.Should().Be((1m, 0m));
    }

    [Test]
    public void GetAxis_UnavailablePrices_AreIgnored()
    {
        // Arrange
        decimal?[] prices = [null, 2m, null];

        // Act
        var axis = GridScaleSelector.GetAxis(prices);

        // Assert
        axis.Should().Be((2m, 0m));
    }

    [Test]
    public void GetAxis_OnlyUnavailablePrices_SpansZeroToOne()
    {
        // Arrange
        decimal?[] prices = [null, null];

        // Act
        var axis = GridScaleSelector.GetAxis(prices);

        // Assert
        axis.Should().Be((1m, 0m));
    }

    [Test]
    public void GetGridScale_EmptyRange_Throws()
    {
        // Arrange
        const decimal bound = 0m;

        // Act
        var act = () => GridScaleSelector.GetGridScale(bound, bound);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
