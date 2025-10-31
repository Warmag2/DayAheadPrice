using System.Diagnostics.CodeAnalysis;
using System.Xml.Serialization;

namespace DayAheadPrice.Entities;

/// <summary>
/// Decoded response from ENTSO-E.
/// </summary>
[SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "This is the name of the incoming document.")]
[Serializable]
[XmlType(AnonymousType = true, Namespace = "urn:iec62325.351:tc57wg16:451-3:publicationdocument:7:3")]
[XmlRoot(Namespace = "urn:iec62325.351:tc57wg16:451-3:publicationdocument:7:3", IsNullable = false)]
internal class Publication_MarketDocument
{
    /// <summary>
    /// The list time series for the ENTSO-E API result.
    /// </summary>
    [XmlElement(ElementName = "TimeSeries")]
    public List<EntsoeTimeSeries> TimeSeries { get; set; } = [];
}
