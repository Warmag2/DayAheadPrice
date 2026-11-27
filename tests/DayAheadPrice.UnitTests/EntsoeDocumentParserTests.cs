using System;
using System.Globalization;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using Faemiyah.DayAheadPrice.Entities;
using Faemiyah.DayAheadPrice.Logic;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Faemiyah.DayAheadPrice.UnitTests;

[TestFixture]
public sealed class EntsoeDocumentParserTests
{
    private const string HourStart = "2025-10-27T23:00Z";
    private const string HourEnd = "2025-10-28T00:00Z";
    private const string NewerNamespace = "urn:iec62325.351:tc57wg16:451-3:publicationdocument:7:4";
    private static readonly DateTime HourStartUtc = Dst.Utc(2025, 10, 27, 23);

    [Test]
    public void Parse_FullPeriod_ReturnsEverySlotInCentsPerKwhKeyedByUtc()
    {
        // Arrange
        var xml = Document(Series("EUR", Period(HourStart, HourEnd, (1, "100.5"), (2, "50"), (3, "-10"), (4, "0"))));

        // Act
        var prices = Parse(xml).Prices;

        // Assert
        prices.Keys.Should().Equal(Quarters(HourStartUtc, 4));
        prices.Keys.Should().AllSatisfy(k => k.Kind.Should().Be(DateTimeKind.Utc));
        prices.Values.Should().Equal(10.05m, 5m, -1m, 0m);
    }

    [Test]
    public void Parse_OmittedPositions_AreForwardFilled()
    {
        // Arrange
        var xml = Document(Series("EUR", Period(HourStart, HourEnd, (1, "10"), (3, "30"))));

        // Act
        var prices = Parse(xml).Prices;

        // Assert
        prices.Values.Should().Equal(1m, 1m, 3m, 3m);
    }

    [Test]
    public void Parse_MissingFirstPosition_LeavesTheLeadingSlotsOut()
    {
        // Arrange
        var xml = Document(Series("EUR", Period(HourStart, HourEnd, (3, "30"))));

        // Act
        var prices = Parse(xml).Prices;

        // Assert
        prices.Keys.Should().Equal(Quarters(HourStartUtc.AddMinutes(30), 2));
        prices.Values.Should().Equal(3m, 3m);
    }

    [Test]
    public void Parse_ForwardFill_DoesNotCarryAcrossPeriods()
    {
        // Arrange: the second period has no price for its first slot.
        var xml = Document(Series(
            "EUR",
            Period(HourStart, HourEnd, (1, "10")),
            Period(HourEnd, "2025-10-28T01:00Z", (2, "20"))));

        // Act
        var prices = Parse(xml).Prices;

        // Assert
        prices.Keys.Should().Equal(Quarters(HourStartUtc, 4).Concat(Quarters(HourStartUtc.AddMinutes(75), 3)));
        prices.Values.Should().Equal(1m, 1m, 1m, 1m, 2m, 2m, 2m);
    }

    [TestCase("2025-10-25T22:00Z", "2025-10-26T23:00Z", 100)]
    [TestCase("2026-03-28T23:00Z", "2026-03-29T22:00Z", 92)]
    public void Parse_DstMarketDay_FillsEveryQuarterOfTheDay(string start, string end, int expectedSlots)
    {
        // Arrange
        var xml = Document(Series("EUR", Period(start, end, (1, "10"))));
        var lastSlotUtc = DateTime.Parse(end, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal).AddMinutes(-15);

        // Act
        var prices = Parse(xml).Prices;

        // Assert
        prices.Should().HaveCount(expectedSlots);
        prices.Keys[^1].Should().Be(lastSlotUtc);
        prices.Values.Should().AllSatisfy(p => p.Should().Be(1m));
    }

    [Test]
    public void Parse_OtherCurrencyOrResolution_IsIgnored()
    {
        // Arrange
        var xml = Document(
            Series("SEK", Period(HourStart, HourEnd, (1, "10"))),
            Series("EUR", Period(HourStart, HourEnd, "PT60M", (1, "10"))));

        // Act
        var prices = Parse(xml).Prices;

        // Assert
        prices.Should().BeEmpty();
    }

    [Test]
    public void Parse_OverlappingSeries_KeepsTheFirstValue()
    {
        // Arrange
        var xml = Document(
            Series("EUR", Period(HourStart, HourEnd, (1, "10"))),
            Series("EUR", Period(HourStart, HourEnd, (1, "20"))));

        // Act
        var prices = Parse(xml).Prices;

        // Assert
        prices.Values.Should().Equal(1m, 1m, 1m, 1m);
    }

    [Test]
    public void Parse_PositionOutsideThePeriod_IsIgnored()
    {
        // Arrange
        var xml = Document(Series("EUR", Period(HourStart, HourEnd, (1, "10"), (5, "50"))));

        // Act
        var prices = Parse(xml).Prices;

        // Assert
        prices.Keys.Should().Equal(Quarters(HourStartUtc, 4));
        prices.Values.Should().Equal(1m, 1m, 1m, 1m);
    }

    [Test]
    public void Parse_UnparsablePrice_Throws()
    {
        // Arrange
        var xml = Document(Series("EUR", Period(HourStart, HourEnd, (1, "n/a"))));

        // Act
        var act = () => Parse(xml);

        // Assert
        act.Should().Throw<InvalidDataException>();
    }

    [Test]
    public void Parse_NotAPublicationDocument_Throws()
    {
        // Arrange
        const string xml = "<SomethingElse />";

        // Act
        var act = () => Parse(xml);

        // Assert
        act.Should().Throw<InvalidDataException>();
    }

    [Test]
    public void Parse_NoMatchingDataAcknowledgement_ReturnsNoPrices()
    {
        // Arrange
        var xml = Acknowledgement("999", "No matching data found for Data item Day-ahead Prices [12.1.D].");

        // Act
        var prices = Parse(xml).Prices;

        // Assert
        prices.Should().BeEmpty();
    }

    [Test]
    public void Parse_ErrorAcknowledgement_ThrowsWithTheReason()
    {
        // Arrange
        var xml = Acknowledgement("999", "Invalid security token.");

        // Act
        var act = () => Parse(xml);

        // Assert
        act.Should().Throw<InvalidDataException>().WithMessage("*Invalid security token.*");
    }

    [Test]
    public void Parse_DocumentInAnotherSchemaVersion_IsParsedAsTheExpectedOne()
    {
        // Arrange
        var xml = DocumentIn(NewerNamespace, Series("EUR", Period(HourStart, HourEnd, (1, "100"))));

        // Act
        var prices = Parse(xml).Prices;

        // Assert
        prices.Keys.Should().Equal(Quarters(HourStartUtc, 4));
        prices.Values.Should().AllBeEquivalentTo(10m);
    }

    [Test]
    public void Parse_DocumentInAnotherSchemaVersionYieldingNoPrices_ThrowsNamingBothNamespaces()
    {
        // Arrange
        var xml = DocumentIn(NewerNamespace, "<RenamedTimeSeries />");

        // Act
        var act = () => Parse(xml);

        // Assert
        act.Should().Throw<InvalidDataException>()
            .WithMessage($"*{NewerNamespace}*{Publication_MarketDocument.XmlNamespace}*");
    }

    private static PriceList Parse(string xml) => EntsoeDocumentParser.Parse(xml, NullLogger.Instance);

    private static string Acknowledgement(string code, string text) =>
        $"""<?xml version="1.0" encoding="UTF-8"?><Acknowledgement_MarketDocument xmlns="urn:iec62325.351:tc57wg16:451-1:acknowledgementdocument:7:0"><Reason><code>{code}</code><text>{text}</text></Reason></Acknowledgement_MarketDocument>""";

    private static string Document(params string[] timeSeries) =>
        DocumentIn(Publication_MarketDocument.XmlNamespace, timeSeries);

    private static string DocumentIn(string xmlNamespace, params string[] timeSeries) =>
        $"""<?xml version="1.0" encoding="UTF-8"?><Publication_MarketDocument xmlns="{xmlNamespace}">{string.Concat(timeSeries)}</Publication_MarketDocument>""";

    private static string Series(string currency, params string[] periods) =>
        $"<TimeSeries><currency_Unit.name>{currency}</currency_Unit.name>{string.Concat(periods)}</TimeSeries>";

    private static string Period(string start, string end, params (int Position, string Price)[] points) =>
        Period(start, end, "PT15M", points);

    private static string Period(string start, string end, string resolution, params (int Position, string Price)[] points) =>
        $"<Period><timeInterval><start>{start}</start><end>{end}</end></timeInterval><resolution>{resolution}</resolution>"
        + string.Concat(points.Select(p => $"<Point><position>{p.Position}</position><price.amount>{p.Price}</price.amount></Point>"))
        + "</Period>";

    private static DateTime[] Quarters(DateTime startUtc, int count) =>
        Enumerable.Range(0, count).Select(i => startUtc.AddMinutes(15 * i)).ToArray();
}
