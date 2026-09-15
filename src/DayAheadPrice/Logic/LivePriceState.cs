using System;
using System.Collections.Generic;
using System.Threading;
using DayAheadPrice.Extensions;

namespace DayAheadPrice.Logic;

/// <summary>
/// An in-memory snapshot of the slots covering the live (current) window.
/// </summary>
/// <param name="MinLocal">The earliest stored local time known when the snapshot was taken.</param>
/// <param name="MaxLocal">The latest stored local time known when the snapshot was taken.</param>
/// <param name="Intervals">The slots, ordered by start, in local time.</param>
internal sealed record LiveSnapshot(
    DateTime MinLocal,
    DateTime MaxLocal,
    IReadOnlyList<(DateTime StartLocal, DateTime EndLocal, decimal Price)> Intervals);

/// <summary>
/// Process-wide cache of the live view's data and the single authority on live-data freshness.
/// </summary>
/// <remarks>
/// The default (current) view is the most frequently requested and the most exposed to abuse, so it must serve
/// from memory without touching the database. Freshness follows the same rule that decides whether new data should
/// be fetched from the API: stored data is fresh while it still reaches more than <see cref="LookaheadHours"/> hours
/// into the future. Registered as a singleton.
/// </remarks>
internal sealed class LivePriceState
{
    /// <summary>
    /// The number of hours of future coverage below which live data is considered stale and worth refreshing.
    /// </summary>
    public const int LookaheadHours = 12;

    private readonly Lock _lock = new();
    private LiveSnapshot? _snapshot;
    private DateTime _lastCheckedHourUtc = DateTime.MinValue;

    /// <summary>
    /// Gets a value indicating whether the cached live data is present and still reaches more than
    /// <see cref="LookaheadHours"/> hours into the future.
    /// </summary>
    public bool IsFresh
    {
        get
        {
            lock (_lock)
            {
                return _snapshot != null && _snapshot.MaxLocal > DateTime.Now.AddHours(LookaheadHours);
            }
        }
    }

    /// <summary>
    /// Gets the current snapshot, or <b>null</b> when nothing has been cached yet.
    /// </summary>
    public LiveSnapshot? Current
    {
        get
        {
            lock (_lock)
            {
                return _snapshot;
            }
        }
    }

    /// <summary>
    /// Replace the cached snapshot.
    /// </summary>
    /// <param name="snapshot">The new snapshot.</param>
    public void Set(LiveSnapshot snapshot)
    {
        lock (_lock)
        {
            _snapshot = snapshot;
        }
    }

    /// <summary>
    /// Drop the cached snapshot, forcing the next live render to reload from the database.
    /// </summary>
    public void Invalidate()
    {
        lock (_lock)
        {
            _snapshot = null;
        }
    }

    /// <summary>
    /// Atomically record an API-refresh check for the current clock hour, returning whether the caller may proceed.
    /// </summary>
    /// <remarks>
    /// At most one check is allowed per clock hour, so a failing or not-yet-updated API is not asked again until the
    /// next hour. The first call in a given hour returns <b>true</b> and marks the hour; later calls in the same hour
    /// return <b>false</b>.
    /// </remarks>
    /// <returns><b>True</b> when a refresh check should proceed.</returns>
    public bool TryBeginRefreshCheck()
    {
        var hourUtc = DateTime.UtcNow.Floor();

        lock (_lock)
        {
            if (hourUtc <= _lastCheckedHourUtc)
            {
                return false;
            }

            _lastCheckedHourUtc = hourUtc;

            return true;
        }
    }
}
