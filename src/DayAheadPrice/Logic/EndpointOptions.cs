namespace DayAheadPrice.Logic;

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
    /// Domain to ask prices for.
    /// </summary>
    public string Domain { get; set; } = string.Empty;
}
