using AwesomeAssertions;
using Faemiyah.DayAheadPrice.Extensions;
using NUnit.Framework;

namespace Faemiyah.DayAheadPrice.UnitTests;

[TestFixture]
public sealed class CssExtensionsTests
{
    [TestCase(0, "0")]
    [TestCase(12.5, "12.5")]
    [TestCase(33.33333, "33.333")]
    [TestCase(-4.25, "-4.25")]
    [SetCulture("fi-FI")]
    public void ToCss_OnACommaDecimalCulture_WritesAnInvariantNonEmptyNumber(decimal value, string expected)
    {
        // Act
        var css = value.ToCss();

        // Assert
        css.Should().Be(expected);
    }
}
