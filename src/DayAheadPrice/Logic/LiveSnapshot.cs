using System;
using System.Collections.Generic;

namespace Faemiyah.DayAheadPrice.Logic;

/// <summary>
/// An in-memory snapshot of the slots covering the live (current) window.
/// </summary>
/// <param name="MinUtc">The earliest stored instant (UTC) known when the snapshot was taken.</param>
/// <param name="MaxUtc">The latest stored instant (UTC) known when the snapshot was taken.</param>
/// <param name="Intervals">The slots, ordered by start, in UTC.</param>
internal sealed record LiveSnapshot(
    DateTime MinUtc,
    DateTime MaxUtc,
    IReadOnlyList<(DateTime StartUtc, DateTime EndUtc, decimal? Price)> Intervals);
