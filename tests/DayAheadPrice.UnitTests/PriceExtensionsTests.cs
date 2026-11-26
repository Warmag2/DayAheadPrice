using AwesomeAssertions;
using Faemiyah.DayAheadPrice.Extensions;
using NUnit.Framework;

namespace Faemiyah.DayAheadPrice.UnitTests;

[TestFixture]
public sealed class PriceExtensionsTests
{
    [Test]
    [SetCulture("fi-FI")]
    public void FormatPrice_OnACommaDecimalCulture_WritesTwoInvariantDecimals()
    {
        // Arrange
        decimal? price = 12.345m;

        // Act
        var text = price.FormatPrice();

        // Assert
        text.Should().Be("12.35c/kWh");
    }

    [Test]
    public void FormatPrice_Unavailable_SaysSo()
    {
        // Arrange
        decimal? price = null;

        // Act
        var text = price.FormatPrice();

        // Assert
        text.Should().Be("unavailable");
    }
}
