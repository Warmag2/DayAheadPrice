using System;
using System.Threading;
using Faemiyah.DayAheadPrice.Extensions;

namespace Faemiyah.DayAheadPrice.Logic;

/// <summary>
/// Process-wide cache of the live view's data and the single authority on live-data freshness.
/// </summary>
/// <remarks>
/// The default (current) view is the most frequently requested and the most exposed to abuse, so it must serve
/// from memory without touching the database. The snapshot mirrors the stored data: every write drops it
/// (<see cref="Invalidate"/>), and it is reloaded only when absent. Freshness is a separate question, used only to
/// decide whether new data should be fetched from the API: stored data is fresh while it still reaches more than
/// <see cref="LookaheadHours"/> hours into the future. This assumes a single app instance writes the database.
/// Registered as a singleton.
/// </remarks>
internal sealed class LivePriceState
{
    /// <summary>
    /// The number of hours of future coverage below which live data is considered stale and worth refreshing.
    /// </summary>
    /// <remarks>
    /// Stored data ends at the end of a market day, midnight CET/CEST (01:00 in Finland). The market (SDAC)
    /// publishes the next day's prices at about 12:45 CET/CEST, so 11 hours makes data go stale at 13:00 CET/CEST,
    /// the first whole hour by which new prices can reasonably be expected. This holds year-round, since the market
    /// day is defined in CET/CEST whatever the display time zone.
    /// </remarks>
    public const int LookaheadHours = 11;

    private readonly Lock _lock = new();
    private readonly TimeProvider _timeProvider;
    private LiveSnapshot? _snapshot;
    private long _version;
    private DateTime _lastCheckedHourUtc = DateTime.MinValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="LivePriceState"/> class.
    /// </summary>
    /// <param name="timeProvider">The source of the current time.</param>
    public LivePriceState(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

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
                return _snapshot != null && _snapshot.MaxUtc > _timeProvider.GetUtcNow().UtcDateTime.AddHours(LookaheadHours);
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
    /// Gets the data version: it changes on every <see cref="Invalidate"/>. Read it before loading a snapshot and pass
    /// it to <see cref="Set"/>.
    /// </summary>
    public long Version
    {
        get
        {
            lock (_lock)
            {
                return _version;
            }
        }
    }

    /// <summary>
    /// Cache a loaded snapshot, unless the data was written (invalidated) after the load began, in which case the
    /// snapshot may miss that write and is discarded.
    /// </summary>
    /// <param name="snapshot">The new snapshot.</param>
    /// <param name="loadedVersion">The <see cref="Version"/> read before the snapshot was loaded.</param>
    public void Set(LiveSnapshot snapshot, long loadedVersion)
    {
        lock (_lock)
        {
            if (_version == loadedVersion)
            {
                _snapshot = snapshot;
            }
        }
    }

    /// <summary>
    /// Drop the cached snapshot, forcing the next live render to reload from the database. Call after every write.
    /// </summary>
    public void Invalidate()
    {
        lock (_lock)
        {
            _snapshot = null;
            _version++;
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
        var hourUtc = _timeProvider.GetUtcNow().UtcDateTime.Floor();

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

    /// <summary>
    /// Undo <see cref="TryBeginRefreshCheck"/> for a check that was abandoned (cancelled) rather than completed, so
    /// the next caller may check again within the same hour.
    /// </summary>
    public void ReleaseRefreshCheck()
    {
        lock (_lock)
        {
            _lastCheckedHourUtc = DateTime.MinValue;
        }
    }
}
