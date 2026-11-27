using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Xml.Serialization;

namespace Faemiyah.DayAheadPrice.Entities;

/// <summary>
/// Decoded response from ENTSO-E.
/// </summary>
[SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "This is just the name of the incoming document.")]
[XmlType(AnonymousType = true, Namespace = XmlNamespace)]
[XmlRoot(Namespace = XmlNamespace, IsNullable = false)]
public class Publication_MarketDocument
{
    /// <summary>
    /// The schema version this type was written against. Documents in another namespace are parsed as if they were
    /// in this one (see <see cref="Logic.EntsoeDocumentParser.Parse(string, Microsoft.Extensions.Logging.ILogger)"/>).
    /// </summary>
    public const string XmlNamespace = "urn:iec62325.351:tc57wg16:451-3:publicationdocument:7:3";

    /// <summary>
    /// The list time series for the ENTSO-E API result.
    /// </summary>
    [XmlElement(ElementName = "TimeSeries")]
    public List<EntsoeTimeSeries> TimeSeries { get; set; } = [];
}
