namespace DayAheadPrice.Entities;

/// <summary>
/// Outcome of an attempt to fetch prices from the ENTSO-e API.
/// </summary>
/// <remarks>
/// Values are shown to the user, so they must not reveal anything about authentication.
/// Authentication failures are reported as <see cref="UnknownError"/> on purpose.
/// </remarks>
internal enum PriceFetchStatus
{
    /// <summary>
    /// Prices were fetched successfully.
    /// </summary>
    Success,

    /// <summary>
    /// The API answered with a server error, which usually means an outage or maintenance break.
    /// </summary>
    ApiUnavailable,

    /// <summary>
    /// The API answered with "not found", which usually means the configured address is wrong.
    /// </summary>
    ApiNotFound,

    /// <summary>
    /// The API could not be reached at all, so no response was received.
    /// </summary>
    Unreachable,

    /// <summary>
    /// The API answered successfully but the response contained no usable prices.
    /// </summary>
    NoDataForPeriod,

    /// <summary>
    /// The response could not be deserialized into the expected document.
    /// </summary>
    InvalidResponse,

    /// <summary>
    /// The fetch failed for any other reason, including authentication failures.
    /// </summary>
    UnknownError
}
