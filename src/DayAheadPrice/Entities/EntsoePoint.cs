using System.Xml.Serialization;

namespace DayAheadPrice.Entities;

[Serializable]
public class EntsoePoint
{
    [XmlElement(ElementName = "position")]
    public int Position { get; set; }

    [XmlElement(ElementName = "price.amount")]
    public string Price { get; set; }
}