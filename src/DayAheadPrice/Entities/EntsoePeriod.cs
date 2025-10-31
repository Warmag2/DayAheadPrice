using System.Xml.Serialization;

namespace DayAheadPrice.Entities;

/// <summary>
/// Entso-e period entity.
/// </summary>
[Serializable]
internal class EntsoePeriod
{
    /// <summary>
    /// The start and end times for this series.
    /// </summary>
    [XmlElement(ElementName = "timeInterval")]
    public EntsoeTimeInterval TimeInterval { get; set; } = new();

    /// <summary>
    /// The data points in the series.
    /// </summary>
    [XmlElement(ElementName = "Point")]
    public List<EntsoePoint> Points { get; set; } = [];

    /// <summary>
    /// The time interval between point indices in the series.
    /// </summary>
    [XmlElement(ElementName = "resolution")]
    public string Resolution { get; set; } = string.Empty;
}
