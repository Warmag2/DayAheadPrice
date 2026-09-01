namespace DayAheadPrice.Entities;

/// <summary>
/// Result of a price fetch, carrying both the prices and the outcome of the attempt.
/// </summary>
/// <param name="Prices">
/// The prices to display, or <c>null</c> when nothing usable is available. This can hold cached
/// prices even when <paramref name="Status"/> is a failure.
/// </param>
/// <param name="Status">Outcome of the fetch attempt.</param>
internal record PriceFetchResult(PriceList? Prices, PriceFetchStatus Status);
