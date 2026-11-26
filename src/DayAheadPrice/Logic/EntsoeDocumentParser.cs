using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;
using Faemiyah.DayAheadPrice.Entities;
using Microsoft.Extensions.Logging;

namespace Faemiyah.DayAheadPrice.Logic;

/// <summary>
/// Turns an ENTSO-E day-ahead price document (A44) into 15-minute prices keyed by UTC slot start.
/// </summary>
internal static class EntsoeDocumentParser
{
    private const string AcknowledgementRoot = "Acknowledgement_MarketDocument";
    private const string NoMatchingDataText = "No matching data found";

    private static readonly XmlSerializer Serializer = new(typeof(Publication_MarketDocument));

    /// <summary>
    /// Whether a response body is the API's answer that it holds no data for the requested range.
    /// </summary>
    /// <remarks>
    /// The API sends this as an acknowledgement document, with either a success or an error status.
    /// </remarks>
    /// <param name="body">The response body.</param>
    /// <returns><b>True</b> when the body reports that no matching data exists.</returns>
    public static bool IsNoMatchingData(string body) => body.Contains(NoMatchingDataText, StringComparison.Ordinal);

    /// <summary>
    /// Deserialize and parse a response body.
    /// </summary>
    /// <param name="xml">The response XML.</param>
    /// <param name="logger">Receives data-quality problems found while parsing.</param>
    /// <returns>The parsed prices; empty when the API reports it has no data for the range.</returns>
    /// <exception cref="InvalidDataException">The API answered with an error acknowledgement, or the XML is not a publication document.</exception>
    public static PriceList Parse(string xml, ILogger logger)
    {
        using var stringReader = new StringReader(xml);
        using var xmlReader = XmlReader.Create(stringReader, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });

        xmlReader.MoveToContent();

        if (xmlReader.LocalName == AcknowledgementRoot)
        {
            if (IsNoMatchingData(xml))
            {
                return new PriceList();
            }

            var reason = XElement.Parse(xml).Descendants().FirstOrDefault(e => e.Name.LocalName == "text")?.Value;

            throw new InvalidDataException($"The API rejected the request: {reason}");
        }

        Publication_MarketDocument document;

        try
        {
            document = Serializer.Deserialize(xmlReader) as Publication_MarketDocument
                ?? throw new InvalidDataException("Could not correctly deserialize data.");
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidDataException("The response is not a valid publication document.", ex);
        }

        return Parse(document, logger);
    }

    /// <summary>
    /// Parse a deserialized document.
    /// </summary>
    /// <remarks>
    /// Only EUR series at 15-minute resolution are read. ENTSO-E omits a position whose price equals the previous
    /// one (curve type A03), so each period is forward-filled from its own points; slots before a period's first point
    /// have no known price and are left out rather than guessed. When several series cover the same slot, the first
    /// one read wins. Parse failures propagate, so the caller can tell a malformed document from an empty one.
    /// </remarks>
    /// <param name="document">The deserialized document.</param>
    /// <param name="logger">Receives data-quality problems found while parsing.</param>
    /// <returns>The parsed prices.</returns>
    public static PriceList Parse(Publication_MarketDocument document, ILogger logger)
    {
        var priceList = new PriceList();

        foreach (var period in document.TimeSeries.Where(t => t.Currency == Market.Currency).SelectMany(t => t.Periods))
        {
            if (period.Resolution == Market.SlotResolutionCode)
            {
                AddPeriod(priceList, period, logger);
            }
        }

        return priceList;
    }

    private static void AddPeriod(PriceList priceList, EntsoePeriod period, ILogger logger)
    {
        var start = period.TimeInterval.StartUtc;
        var slotCount = (int)((period.TimeInterval.EndUtc - start).Ticks / Market.SlotLength.Ticks);
        var byPosition = new Dictionary<int, decimal>();

        foreach (var point in period.Points)
        {
            if (point.Position < 1 || point.Position > slotCount)
            {
                logger.LogError("Ignoring price at position {Position}, outside the period starting {Start} ({Slots} slots).", point.Position, start, slotCount);

                continue;
            }

            var price = point.Price * Market.EurPerMwhToCentsPerKwh;

            if (byPosition.TryGetValue(point.Position, out var oldPrice))
            {
                logger.LogError("Attempting to add price where it already exists ({Time}): {Old}, {New}", start + (point.Position - 1) * Market.SlotLength, oldPrice, price);
            }

            byPosition[point.Position] = price;
        }

        decimal? lastPrice = null;

        for (var position = 1; position <= slotCount; position++)
        {
            if (byPosition.TryGetValue(position, out var price))
            {
                lastPrice = price;
            }

            if (lastPrice.HasValue)
            {
                AddToSeries(priceList, start + (position - 1) * Market.SlotLength, lastPrice.Value, logger);
            }
        }
    }

    private static void AddToSeries(PriceList priceList, DateTime slotUtc, decimal price, ILogger logger)
    {
        if (priceList.Prices.TryGetValue(slotUtc, out var oldPrice))
        {
            if (oldPrice != price)
            {
                logger.LogError("Trying to add duplicate differing value for time {Time}. Existing: {OldPrice}, New: {NewPrice}", slotUtc, oldPrice, price);
            }
        }
        else
        {
            priceList.Prices.Add(slotUtc, price);
        }
    }
}
