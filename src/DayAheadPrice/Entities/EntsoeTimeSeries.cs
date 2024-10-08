using System.Xml.Serialization;

namespace DayAheadPrice.Entities;

[Serializable]
public class EntsoeTimeSeries
{
    /// <summary>
    /// The currency for this timeseries result.
    /// </summary>
    [XmlElement(ElementName = "currency_Unit.name")]
    public string Currency { get; set; }

    /// <summary>
    /// The periods inside a time series.
    /// </summary>
    [XmlElement(ElementName = "Period")]
    public List<EntsoePeriod> Periods { get; set; } = new();
}
