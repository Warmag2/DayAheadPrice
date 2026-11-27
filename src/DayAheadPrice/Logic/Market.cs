using System;

namespace Faemiyah.DayAheadPrice.Logic;

/// <summary>
/// Fixed facts about the day-ahead market data this application reads and stores.
/// </summary>
internal static class Market
{
    /// <summary>
    /// The only currency read and stored.
    /// </summary>
    public const string Currency = "EUR";

    /// <summary>
    /// The ENTSO-E resolution code of the only slot length read and stored.
    /// </summary>
    public const string SlotResolutionCode = "PT15M";

    /// <summary>
    /// Converts a price in €/MWh, as ENTSO-E publishes it, into c/kWh, as it is stored.
    /// </summary>
    public const decimal EurPerMwhToCentsPerKwh = 0.1m;

    /// <summary>
    /// The length of one stored slot: one quarter hour.
    /// </summary>
    public static readonly TimeSpan SlotLength = CalendarStep.QuarterLength;

    /// <summary>
    /// The first slot fetched: the start of delivery day 2025-10-01 (CEST), when SDAC moved to a 15-minute market
    /// time unit. Earlier days are published hourly only, so history is never fetched from before this instant.
    /// </summary>
    public static readonly DateTime HistoryStartUtc = new(2025, 9, 30, 22, 0, 0, DateTimeKind.Utc);
}
