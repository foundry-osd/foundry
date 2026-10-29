// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Connect.Tests.Support;

/// <summary>
/// Deterministic clock: timers fire synchronously inside <see cref="Advance"/> (same contract as FakeTimeProvider).
/// </summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now;

    public ManualTimeProvider()
        : this(new DateTimeOffset(2026, 10, 29, 8, 0, 0, TimeSpan.Zero))
    {
    }

    public ManualTimeProvider(DateTimeOffset start) => _now = start;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    // Lets a test wait until the code under test has armed its timer before advancing the clock.
    public int ActiveTimerCount
    {
        get
        {
            lock (_gate)
            {
                return _timers.Count(static timer => timer.DueAt is not null);
            }
        }
    }

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _now;
        }
    }

    public override long GetTimestamp() => GetUtcNow().UtcTicks;

    public void Advance(TimeSpan delta)
    {
        List<ManualTimer> due;
        DateTimeOffset now;
        lock (_gate)
        {
            _now += delta;
            now = _now;
            due = _timers.Where(timer => timer.DueAt is { } dueAt && dueAt <= now).OrderBy(static timer => timer.DueAt).ToList();
        }

        foreach (ManualTimer timer in due)
        {
            timer.Fire(now);
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        lock (_gate)
        {
            _timers.Add(timer);
        }

        timer.Change(dueTime, period);
        return timer;
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        private TimeSpan _period = Timeout.InfiniteTimeSpan;

        public DateTimeOffset? DueAt { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner._gate)
            {
                DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : owner._now + dueTime;
                _period = period;
            }

            return true;
        }

        public void Fire(DateTimeOffset now)
        {
            lock (owner._gate)
            {
                DueAt = _period > TimeSpan.Zero && _period != Timeout.InfiniteTimeSpan ? now + _period : null;
            }

            callback(state);
        }

        public void Dispose()
        {
            lock (owner._gate)
            {
                DueAt = null;
                owner._timers.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
