// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics.CodeAnalysis;

namespace Foundry.AutopilotInteractive.Tests.Support;

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

    /// <summary>
    /// Moves the clock forward, firing due timers one at a time in due order. The clock reads each timer's due time while
    /// its callback runs, then the final time. A periodic timer fires once per period that fits in the interval.
    /// </summary>
    public void Advance(TimeSpan delta)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(delta, TimeSpan.Zero);

        DateTimeOffset target;
        lock (_gate)
        {
            target = _now + delta;
        }

        // The next timer is picked under the lock on every step, so a timer disposed or re-armed by an earlier callback is
        // never fired from a stale snapshot.
        while (TryTakeNextDue(target, out ManualTimer? timer))
        {
            timer.Invoke();
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        lock (_gate)
        {
            _timers.Add(timer);
        }

        return timer;
    }

    private bool TryTakeNextDue(DateTimeOffset target, [NotNullWhen(true)] out ManualTimer? next)
    {
        lock (_gate)
        {
            next = null;
            foreach (ManualTimer timer in _timers)
            {
                if (timer.DueAt is { } dueAt && dueAt <= target && (next is null || dueAt < next.DueAt))
                {
                    next = timer;
                }
            }

            if (next is null)
            {
                _now = target;
                return false;
            }

            DateTimeOffset due = next.DueAt!.Value;
            if (due > _now)
            {
                _now = due;
            }

            // Re-arm before the callback runs so the callback can still dispose or change the timer.
            next.AdvanceAfterFire(due);
            return true;
        }
    }

    private static void ValidateInterval(TimeSpan value, string parameterName)
    {
        if (value != Timeout.InfiniteTimeSpan)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, TimeSpan.Zero, parameterName);
        }
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        private TimeSpan _period = Timeout.InfiniteTimeSpan;

        public DateTimeOffset? DueAt { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            ValidateInterval(dueTime, nameof(dueTime));
            ValidateInterval(period, nameof(period));

            lock (owner._gate)
            {
                DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : owner._now + dueTime;
                _period = period;
            }

            return true;
        }

        // Called with the owner gate held.
        public void AdvanceAfterFire(DateTimeOffset firedAt) =>
            DueAt = _period > TimeSpan.Zero ? firedAt + _period : null;

        public void Invoke() => callback(state);

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
