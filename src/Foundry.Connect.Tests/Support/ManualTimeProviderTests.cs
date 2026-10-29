// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Connect.Tests.Support;

public sealed class ManualTimeProviderTests
{
    [Fact]
    public void Advance_FiresDueTimersInDueOrder()
    {
        var clock = new ManualTimeProvider();
        var fired = new List<string>();
        using ITimer late = clock.CreateTimer(_ => fired.Add("late"), null, TimeSpan.FromSeconds(10), Timeout.InfiniteTimeSpan);
        using ITimer early = clock.CreateTimer(_ => fired.Add("early"), null, TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);
        using ITimer notDue = clock.CreateTimer(_ => fired.Add("notDue"), null, TimeSpan.FromSeconds(60), Timeout.InfiniteTimeSpan);

        clock.Advance(TimeSpan.FromSeconds(30));

        Assert.Equal(["early", "late"], fired);
    }

    [Fact]
    public void Advance_CallbackSeesItsOwnDueTimeAndTheClockEndsAtTheTarget()
    {
        var clock = new ManualTimeProvider();
        DateTimeOffset start = clock.GetUtcNow();
        var seen = new List<DateTimeOffset>();
        using ITimer first = clock.CreateTimer(_ => seen.Add(clock.GetUtcNow()), null, TimeSpan.FromSeconds(3), Timeout.InfiniteTimeSpan);
        using ITimer second = clock.CreateTimer(_ => seen.Add(clock.GetUtcNow()), null, TimeSpan.FromSeconds(8), Timeout.InfiniteTimeSpan);

        clock.Advance(TimeSpan.FromSeconds(20));

        Assert.Equal([start.AddSeconds(3), start.AddSeconds(8)], seen);
        Assert.Equal(start.AddSeconds(20), clock.GetUtcNow());
    }

    [Fact]
    public void Advance_DoesNotFireATimerDisposedByAnEarlierCallback()
    {
        var clock = new ManualTimeProvider();
        var fired = new List<string>();
        ITimer? victim = null;
        using ITimer killer = clock.CreateTimer(
            _ =>
            {
                fired.Add("killer");
                victim!.Dispose();
            },
            null,
            TimeSpan.FromSeconds(1),
            Timeout.InfiniteTimeSpan);
        victim = clock.CreateTimer(_ => fired.Add("victim"), null, TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);

        clock.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(["killer"], fired);
    }

    [Fact]
    public void Advance_DoesNotFireATimerRearmedLaterByAnEarlierCallback()
    {
        var clock = new ManualTimeProvider();
        var fired = new List<string>();
        ITimer? postponed = null;
        using ITimer mover = clock.CreateTimer(
            _ => postponed!.Change(TimeSpan.FromSeconds(100), Timeout.InfiniteTimeSpan),
            null,
            TimeSpan.FromSeconds(1),
            Timeout.InfiniteTimeSpan);
        postponed = clock.CreateTimer(_ => fired.Add("postponed"), null, TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);

        clock.Advance(TimeSpan.FromSeconds(5));

        Assert.Empty(fired);
        using (postponed)
        {
            clock.Advance(TimeSpan.FromSeconds(100));
            Assert.Equal(["postponed"], fired);
        }
    }

    [Fact]
    public void Advance_FiresAPeriodicTimerOncePerPeriodThatFits()
    {
        var clock = new ManualTimeProvider();
        DateTimeOffset start = clock.GetUtcNow();
        var seen = new List<DateTimeOffset>();
        using ITimer periodic = clock.CreateTimer(
            _ => seen.Add(clock.GetUtcNow()), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3));

        clock.Advance(TimeSpan.FromSeconds(10));

        Assert.Equal([start.AddSeconds(2), start.AddSeconds(5), start.AddSeconds(8)], seen);
    }

    [Fact]
    public void Advance_WhenDeltaIsNegative_Throws()
    {
        var clock = new ManualTimeProvider();

        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void CreateTimer_WhenDueTimeOrPeriodIsInvalid_Throws()
    {
        var clock = new ManualTimeProvider();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => clock.CreateTimer(_ => { }, null, TimeSpan.FromSeconds(-2), Timeout.InfiniteTimeSpan));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => clock.CreateTimer(_ => { }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(-2)));
    }
}
