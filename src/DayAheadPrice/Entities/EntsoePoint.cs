using System.Xml.Serialization;

namespace Faemiyah.DayAheadPrice.Entities;

/// <summary>
/// ENTSO-e data point entity.
/// </summary>
public class EntsoePoint
{
    /// <summary>
    /// The numeric position index.
    /// </summary>
    [XmlElement(ElementName = "position")]
    public int Position { get; set; }

    /// <summary>
    /// Price at this position index, in EUR/MWh.
    /// </summary>
    [XmlElement(ElementName = "price.amount")]
    public decimal Price { get; set; }
}