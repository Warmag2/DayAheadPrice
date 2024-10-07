using System.Xml.Serialization;

namespace DayAheadPrice.Entities;

[Serializable]
public class EntsoeTimeSeries
{
    [XmlElement(ElementName = "currency_Unit.name")]
    public string Currency { get; set; }

    [XmlElement(ElementName = "Period")]
    public EntsoePeriod Period { get; set; } = new();
}
