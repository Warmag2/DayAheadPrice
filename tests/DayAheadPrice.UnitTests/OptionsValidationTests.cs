using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using AwesomeAssertions;
using Faemiyah.DayAheadPrice.Options;
using NUnit.Framework;

namespace Faemiyah.DayAheadPrice.UnitTests;

[TestFixture]
public sealed class OptionsValidationTests
{
    [TestCase("")]
    [TestCase("web-api.tp.entsoe.eu")]
    [TestCase("ftp://web-api.tp.entsoe.eu/")]
    public void EndpointOptions_ApiAddressNotAnAbsoluteHttpUrl_IsInvalid(string apiAddress)
    {
        // Arrange
        var options = ValidEndpointOptions();
        options.ApiAddress = apiAddress;

        // Act
        var invalidMembers = InvalidMembers(options);

        // Assert
        invalidMembers.Should().Equal(nameof(EndpointOptions.ApiAddress));
    }

    [TestCase(false, true)]
    [TestCase(true, false)]
    public void EndpointOptions_NoApiKey_IsInvalidOnlyWhenTheApiIsCalled(bool generateTestData, bool expectInvalid)
    {
        // Arrange
        var options = ValidEndpointOptions();
        options.ApiKey = string.Empty;
        options.GenerateTestData = generateTestData;

        // Act
        var invalidMembers = InvalidMembers(options);

        // Assert
        invalidMembers.Contains(nameof(EndpointOptions.ApiKey)).Should().Be(expectInvalid);
    }

    [Test]
    public void PricingOptions_NegativeVat_IsInvalid()
    {
        // Arrange
        var options = new PricingOptions { Margin = 0.49m, Vat = -0.1m };

        // Act
        var invalidMembers = InvalidMembers(options);

        // Assert
        invalidMembers.Should().Equal(nameof(PricingOptions.Vat));
    }

    [Test]
    public void DisplayOptions_UnknownTimeZone_IsInvalid()
    {
        // Arrange
        var options = new DisplayOptions { TimeZone = "Europe/Atlantis" };

        // Act
        var invalidMembers = InvalidMembers(options);

        // Assert
        invalidMembers.Should().Equal(nameof(DisplayOptions.TimeZone));
    }

    private static EndpointOptions ValidEndpointOptions() => new()
    {
        ApiAddress = "https://web-api.tp.entsoe.eu/",
        ApiKey = "key",
        Domain = "10YFI-1--------U"
    };

    private static List<string> InvalidMembers(object options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);

        return results.SelectMany(r => r.MemberNames).ToList();
    }
}
