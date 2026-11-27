using System;
using System.Globalization;
using System.Xml.Serialization;

namespace Faemiyah.DayAheadPrice.Entities;

/// <summary>
/// Time interval for ENTSO-E import data.
/// </summary>
/// <remarks>
/// The times are kept as strings because ENTSO-E writes them without seconds (<c>2025-10-25T22:00Z</c>), which
/// <see cref="XmlSerializer"/> does not accept as <c>xs:dateTime</c>.
/// </remarks>
public class EntsoeTimeInterval
{
    /// <summary>
    /// Start time in XML, e.g. <c>2025-10-25T22:00Z</c>.
    /// </summary>
    [XmlElement(ElementName = "start")]
    public string Start { get; set; } = string.Empty;

    /// <summary>
    /// End time in XML, e.g. <c>2025-10-26T23:00Z</c>.
    /// </summary>
    [XmlElement(ElementName = "end")]
    public string End { get; set; } = string.Empty;

    /// <summary>
    /// Gets the start time as a UTC datetime.
    /// </summary>
    /// <exception cref="FormatException"><see cref="Start"/> is not a valid time.</exception>
    [XmlIgnore]
    public DateTime StartUtc => ParseUtc(Start);

    /// <summary>
    /// Gets the end time as a UTC datetime.
    /// </summary>
    /// <exception cref="FormatException"><see cref="End"/> is not a valid time.</exception>
    [XmlIgnore]
    public DateTime EndUtc => ParseUtc(End);

    private static DateTime ParseUtc(string value) =>
        DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
}

