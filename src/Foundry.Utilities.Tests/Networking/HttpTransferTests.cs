// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Utilities.Networking;

namespace Foundry.Utilities.Tests.Networking;

public sealed class HttpTransferTests
{
    [Theory]
    [InlineData(0, "overall")]
    [InlineData(1, "inactivity")]
    public async Task RunAsync_WhenDeadlineExpires_CancelsUnderlyingWorkAndWaitsForCleanup(int timerIndex, string reason)
    {
        var clock = new ControlledTimeProvider();
        var cleaningUp = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanupFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var guard = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        Task<int> transfer = HttpTransfer.RunAsync(async (token, _) =>
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return 0;
            }
            finally
            {
                cleaningUp.SetResult();
                await cleanupFinished.Task;
            }
        }, guard.Token, clock);

        try
        {
            Assert.Equal(2, clock.Timers.Count);
            clock.Timers[timerIndex].Fire();
            await cleaningUp.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.False(transfer.IsCompleted);
            cleanupFinished.SetResult();
            TimeoutException error = await Assert.ThrowsAsync<TimeoutException>(() => transfer);
            Assert.Contains(reason, error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.All(clock.Timers, timer => Assert.True(timer.Disposed));
        }
        finally
        {
            guard.Cancel();
            cleanupFinished.TrySetResult();
            try { await transfer; } catch (Exception) { }
        }
    }

    [Fact]
    public async Task RunAsync_WhenTransferProgressesSlowly_AllowsMoreThanOneInactivityPeriod()
    {
        var clock = new ControlledTimeProvider();
        int result = await HttpTransfer.RunAsync((token, progress) =>
        {
            for (int chunk = 0; chunk < 5; chunk++)
            {
                clock.Advance(TimeSpan.FromMinutes(1));
                token.ThrowIfCancellationRequested();
                progress();
            }
            return Task.FromResult(7);
        }, TestContext.Current.CancellationToken, clock);
        Assert.Equal(7, result);
        Assert.All(clock.Timers, timer => Assert.True(timer.Disposed));
    }

    [Fact]
    public async Task RunAsync_WhenProgressContinues_StillEnforcesOverallDeadline()
    {
        var clock = new ControlledTimeProvider();
        TimeoutException failure = await Assert.ThrowsAsync<TimeoutException>(() => HttpTransfer.RunAsync((token, progress) =>
        {
            for (int chunk = 0; chunk < 30; chunk++)
            {
                clock.Advance(TimeSpan.FromMinutes(1));
                token.ThrowIfCancellationRequested();
                progress();
            }
            return Task.FromResult(true);
        }, TestContext.Current.CancellationToken, clock));
        Assert.Contains("overall", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_WhenCallerCancels_PreservesCancellationInsteadOfTimeout()
    {
        var clock = new ControlledTimeProvider();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        Task<bool> transfer = HttpTransfer.RunAsync(async (token, _) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return true;
        }, caller.Token, clock);
        caller.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transfer);
    }

    private sealed class ControlledTimeProvider : TimeProvider
    {
        public TimeSpan Elapsed { get; private set; }
        public List<ControlledTimer> Timers { get; } = [];

        public void Advance(TimeSpan elapsed)
        {
            Elapsed += elapsed;
            foreach (ControlledTimer timer in Timers.ToArray())
            {
                if (!timer.Disposed && timer.DueAt <= Elapsed) timer.Fire();
            }
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ControlledTimer(this, callback, state, dueTime);
            Timers.Add(timer);
            return timer;
        }
    }

    private sealed class ControlledTimer(ControlledTimeProvider clock, TimerCallback callback, object? state, TimeSpan dueTime) : ITimer
    {
        public TimeSpan DueAt { get; private set; } = clock.Elapsed + dueTime;
        public bool Disposed { get; private set; }
        public void Fire() => callback(state);
        public bool Change(TimeSpan nextDueTime, TimeSpan period)
        {
            DueAt = clock.Elapsed + nextDueTime;
            return true;
        }
        public void Dispose() => Disposed = true;
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
