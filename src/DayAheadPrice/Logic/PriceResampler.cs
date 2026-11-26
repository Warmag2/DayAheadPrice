using System;
using System.Collections.Generic;
using System.Linq;
using Faemiyah.DayAheadPrice.Database.Entities;

namespace Faemiyah.DayAheadPrice.Logic;

/// <summary>
/// Pure helpers that lay stored price intervals onto a calendar grid of display bars.
/// </summary>
internal static class PriceResampler
{
    /// <summary>
    /// The stored slots as intervals ordered by start, skipping any slot without both ends.
    /// </summary>
    /// <param name="slots">The stored slots.</param>
    /// <returns>The intervals, in UTC; a <b>null</b> price marks an unavailable slot.</returns>
    public static List<(DateTime StartUtc, DateTime EndUtc, decimal? Price)> ToIntervals(this IEnumerable<PricePoint> slots) => slots
        .Where(s => s.DateBegin.HasValue && s.DateEnd.HasValue)
        .Select(s => (StartUtc: s.DateBegin!.Value, EndUtc: s.DateEnd!.Value, s.Price))
        .OrderBy(i => i.StartUtc)
        .ToList();

    /// <summary>
    /// The successive bar starts from <paramref name="fromUtc"/> (inclusive) up to <paramref name="toUtc"/>
    /// (exclusive).
    /// </summary>
    /// <param name="fromUtc">The first bar start, UTC.</param>
    /// <param name="toUtc">The exclusive end, UTC.</param>
    /// <param name="resolution">The width of one bar.</param>
    /// <param name="zone">The time zone whose calendar the bars follow.</param>
    /// <returns>The bar starts, UTC.</returns>
    public static IEnumerable<DateTime> EnumerateBarStarts(DateTime fromUtc, DateTime toUtc, CalendarStep resolution, TimeZoneInfo zone)
    {
        for (var barStart = fromUtc; barStart < toUtc; barStart = resolution.AddTo(barStart, zone))
        {
            yield return barStart;
        }
    }

    /// <summary>
    /// Compute the contiguous time ranges within <paramref name="fromUtc"/>..<paramref name="toUtc"/> that no
    /// stored slot covers.
    /// </summary>
    /// <param name="slots">The stored slots overlapping the window.</param>
    /// <param name="fromUtc">The inclusive window start, UTC.</param>
    /// <param name="toUtc">The exclusive window end, UTC.</param>
    /// <returns>The missing ranges, ordered, each an inclusive start / exclusive end in UTC.</returns>
    public static List<(DateTime FromUtc, DateTime ToUtc)> ComputeGaps(
        IReadOnlyCollection<PricePoint> slots,
        DateTime fromUtc,
        DateTime toUtc)
    {
        var gaps = new List<(DateTime FromUtc, DateTime ToUtc)>();
        var cursor = fromUtc;

        foreach (var (slotStart, slotEnd, _) in slots.ToIntervals())
        {
            if (slotStart > cursor)
            {
                gaps.Add((cursor, slotStart < toUtc ? slotStart : toUtc));
            }

            if (slotEnd > cursor)
            {
                cursor = slotEnd;
            }

            if (cursor >= toUtc)
            {
                return gaps;
            }
        }

        if (cursor < toUtc)
        {
            gaps.Add((cursor, toUtc));
        }

        return gaps;
    }

    /// <summary>
    /// Resample a set of intervals onto a calendar grid, without any database access or caching.
    /// </summary>
    /// <param name="intervals">The intervals, ordered by start, in UTC; a <b>null</b> price marks an unavailable slot.</param>
    /// <param name="fromUtc">The inclusive window start, UTC.</param>
    /// <param name="toUtc">The exclusive window end, UTC.</param>
    /// <param name="resolution">The width of one bar.</param>
    /// <param name="zone">The time zone whose calendar the bars follow.</param>
    /// <returns>
    /// The bars, keyed by UTC start, omitting any bar with no data; a bar covered only by unavailable slots has a
    /// <b>null</b> value.
    /// </returns>
    public static SortedList<DateTime, decimal?> ResampleIntervals(
        IReadOnlyList<(DateTime StartUtc, DateTime EndUtc, decimal? Price)> intervals,
        DateTime fromUtc,
        DateTime toUtc,
        CalendarStep resolution,
        TimeZoneInfo zone)
    {
        var bars = new SortedList<DateTime, decimal?>();

        foreach (var barStart in EnumerateBarStarts(fromUtc, toUtc, resolution, zone))
        {
            var barEnd = resolution.AddTo(barStart, zone);
            var bar = ComputeBar(intervals, barStart, barEnd);

            if (bar.CoveredTicks > 0)
            {
                bars[barStart] = bar.Value;
            }
        }

        return bars;
    }

    /// <summary>
    /// Time-weighted average of the overlapping intervals for a single bar.
    /// </summary>
    /// <remarks>
    /// Unavailable slots (<b>null</b> price) count towards coverage, since they are known and final, but not towards
    /// the average, which is taken over the priced time only.
    /// </remarks>
    /// <param name="intervals">The stored intervals, ordered by start; a <b>null</b> price marks an unavailable slot.</param>
    /// <param name="barStart">The inclusive bar start, UTC.</param>
    /// <param name="barEnd">The exclusive bar end, UTC.</param>
    /// <returns>
    /// The average over the priced time (<b>null</b> when nothing in the bar is priced), the ticks covered by any
    /// slot, and the ticks covered by priced slots.
    /// </returns>
    public static (decimal? Value, long CoveredTicks, long PricedTicks) ComputeBar(
        IReadOnlyList<(DateTime StartUtc, DateTime EndUtc, decimal? Price)> intervals,
        DateTime barStart,
        DateTime barEnd)
    {
        var weighted = 0m;
        long coveredTicks = 0;
        long pricedTicks = 0;

        foreach (var (intervalStart, intervalEnd, intervalPrice) in intervals)
        {
            if (intervalEnd <= barStart)
            {
                continue;
            }

            if (intervalStart >= barEnd)
            {
                break;
            }

            var overlapStart = intervalStart > barStart ? intervalStart : barStart;
            var overlapEnd = intervalEnd < barEnd ? intervalEnd : barEnd;
            var ticks = (overlapEnd - overlapStart).Ticks;

            if (ticks <= 0)
            {
                continue;
            }

            coveredTicks += ticks;

            if (intervalPrice is { } price)
            {
                weighted += price * ticks;
                pricedTicks += ticks;
            }
        }

        return (pricedTicks > 0 ? weighted / pricedTicks : null, coveredTicks, pricedTicks);
    }
}
