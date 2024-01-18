using System.Xml.Serialization;

namespace DayAheadPrice.Entities;

[Serializable]
public class EntsoeTimeSeries
{
    [XmlElement(ElementName = "Period")]
    public EntsoePeriod Period { get; set; } = new();
}
