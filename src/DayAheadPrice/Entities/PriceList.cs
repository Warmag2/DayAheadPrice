using System;
using System.Collections.Generic;

namespace Faemiyah.DayAheadPrice.Entities;

/// <summary>
/// Prices parsed from one API response (or generated test data), before they are persisted.
/// </summary>
internal class PriceList
{
    /// <summary>
    /// Prices per 15-minute slot, keyed by the UTC slot start.
    /// </summary>
    public SortedList<DateTime, decimal> Prices { get; set; } = [];
}
