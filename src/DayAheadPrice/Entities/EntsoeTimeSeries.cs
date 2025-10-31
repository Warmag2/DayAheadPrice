using System.Xml.Serialization;

namespace DayAheadPrice.Entities;

/// <summary>
/// Entso-e time series entity.
/// </summary>
[Serializable]
internal class EntsoeTimeSeries
{
    /// <summary>
    /// The currency for this timeseries result.
    /// </summary>
    [XmlElement(ElementName = "currency_Unit.name")]
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// The periods inside a time series.
    /// </summary>
    [XmlElement(ElementName = "Period")]
    public List<EntsoePeriod> Periods { get; set; } = [];
}
