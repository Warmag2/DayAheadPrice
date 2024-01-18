using System.Xml.Serialization;

namespace DayAheadPrice.Entities;

[Serializable]
public class EntsoePeriod
{
    [XmlElement(ElementName = "timeInterval")]
    public EntsoeTimeInterval TimeInterval { get; set; } = new();

    [XmlElement(ElementName = "Point")]
    public List<EntsoePoint> Points { get; set; } = new();
}
