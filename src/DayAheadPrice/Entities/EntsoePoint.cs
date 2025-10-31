using System.Xml.Serialization;

namespace DayAheadPrice.Entities;

/// <summary>
/// Entso-e data point entity.
/// </summary>
[Serializable]
public class EntsoePoint
{
    /// <summary>
    /// The numeric position index.
    /// </summary>
    [XmlElement(ElementName = "position")]
    public int Position { get; set; }

    /// <summary>
    /// Price at this position index.
    /// </summary>
    [XmlElement(ElementName = "price.amount")]
    public string Price { get; set; } = string.Empty;
}