using System;
using AwesomeAssertions;
using Faemiyah.DayAheadPrice.Logic;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;

namespace Faemiyah.DayAheadPrice.UnitTests;

[TestFixture]
public sealed class LivePriceStateTests
{
    private static readonly DateTime NowUtc = Dst.Utc(2025, 10, 27, 10, 20);
    private static readonly LiveSnapshot Snapshot = new(NowUtc, NowUtc, []);

    private FakeTimeProvider _clock = null!;
    private LivePriceState _state = null!;

    [SetUp]
    public void SetUp()
    {
        _clock = new FakeTimeProvider(new DateTimeOffset(NowUtc));
        _state = new LivePriceState(_clock);
    }

    [Test]
    public void Set_NoWriteSinceTheLoadBegan_CachesTheSnapshot()
    {
        // Arrange
        var version = _state.Version;

        // Act
        _state.Set(Snapshot, version);

        // Assert
        _state.Current.Should().BeSameAs(Snapshot);
    }

    [Test]
    public void Set_WriteDuringTheLoad_DiscardsTheSnapshot()
    {
        // Arrange
        var version = _state.Version;
        _state.Invalidate();

        // Act
        _state.Set(Snapshot, version);

        // Assert
        _state.Current.Should().BeNull();
    }

    [TestCase(LivePriceState.LookaheadHours * 60 + 1, true)]
    [TestCase(LivePriceState.LookaheadHours * 60, false)]
    public void IsFresh_SnapshotReachingAheadByMinutes_IsFreshOnlyBeyondTheLookahead(int minutesAhead, bool expected)
    {
        // Arrange
        _state.Set(new LiveSnapshot(NowUtc.AddDays(-1), NowUtc.AddMinutes(minutesAhead), []), _state.Version);

        // Act
        var isFresh = _state.IsFresh;

        // Assert
        isFresh.Should().Be(expected);
    }

    [TestCase(39, false)]
    [TestCase(40, true)]
    public void TryBeginRefreshCheck_AfterACheck_IsAllowedAgainOnlyInTheNextClockHour(int minutesLater, bool expected)
    {
        // Arrange: the first check is at 10:20, so 40 minutes later is 11:00.
        _state.TryBeginRefreshCheck();
        _clock.Advance(TimeSpan.FromMinutes(minutesLater));

        // Act
        var allowed = _state.TryBeginRefreshCheck();

        // Assert
        allowed.Should().Be(expected);
    }

    [Test]
    public void TryBeginRefreshCheck_AfterAReleasedCheck_AllowsAnotherCheckInTheSameHour()
    {
        // Arrange
        _state.TryBeginRefreshCheck();
        _state.ReleaseRefreshCheck();

        // Act
        var allowed = _state.TryBeginRefreshCheck();

        // Assert
        allowed.Should().BeTrue();
    }
}
