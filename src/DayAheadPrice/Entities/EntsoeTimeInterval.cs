using System.Globalization;
using System.Xml.Serialization;

namespace DayAheadPrice.Entities;

/// <summary>
/// Time interval for ENTSO-E import data.
/// </summary>
[Serializable]
internal class EntsoeTimeInterval
{
    private string _startField = string.Empty;

    private string _endField = string.Empty;

    /// <summary>
    /// Start time as datetime.
    /// </summary>
    public DateTime StartDateTime { get; set; }

    /// <summary>
    /// End time as datetime.
    /// </summary>
    public DateTime EndDateTime { get; set; }

    /// <summary>
    /// Start time in XML.
    /// </summary>
    [XmlElement(ElementName = "start")]
    public string Start
    {
        get => _startField; set
        {
            _startField = value;

            var us = new CultureInfo("en-US");
            StartDateTime = DateTime.Parse(value, us);
        }
    }

    /// <summary>
    /// End time in XML.
    /// </summary>
    [XmlElement(ElementName = "end")]
    public string End
    {
        get => _endField; set
        {
            _endField = value;

            var us = new CultureInfo("en-US");
            EndDateTime = DateTime.Parse(value, us);
        }
    }
}
