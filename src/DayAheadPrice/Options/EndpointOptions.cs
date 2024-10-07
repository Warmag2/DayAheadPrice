namespace DayAheadPrice.Options;

/// <summary>
/// Options for fetching data from the ensoe API.
/// </summary>
public class EndpointOptions
{
    /// <summary>
    /// API root URL.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// API key.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Business type.
    /// </summary>
    /// <remarks>
    /// Use default if you do not know what this means.
    /// </remarks>
    public string BusinessType { get; set; } = "A62";


    /// <summary>
    /// Domain type to ask.
    /// </summary>
    /// <remarks>
    /// Use default if you do not know what this means.
    /// </remarks>
    public string DocumentType { get; set; } = "A44";

    /// <summary>
    /// Domain to ask prices for.
    /// </summary>
    public string Domain { get; set; } = string.Empty;

    /// <summary>
    /// Process type.
    /// </summary>
    /// <remarks>
    /// Use default if you do not know what this means.
    /// </remarks>
    public string ProcessType { get; set; } = "A01";
}
