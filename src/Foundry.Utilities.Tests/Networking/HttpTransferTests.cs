// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Utilities.Networking;

namespace Foundry.Utilities.Tests.Networking;

public sealed class HttpTransferTests
{
    [Fact]
    public async Task RunAsync_WhenInactivityDeadlineExpires_CancelsUnderlyingWorkAndWaitsForCleanup()
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
            clock.Advance(TimeSpan.FromMinutes(2));
            await cleaningUp.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.False(transfer.IsCompleted);
            cleanupFinished.SetResult();
            TimeoutException error = await Assert.ThrowsAsync<TimeoutException>(() => transfer);
            Assert.Contains("inactivity", error.Message, StringComparison.OrdinalIgnoreCase);
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
    public async Task RunAsync_WhenProgressStops_TimesOutTwoMinutesAfterLastProgress()
    {
        var clock = new ControlledTimeProvider();
        TimeoutException failure = await Assert.ThrowsAsync<TimeoutException>(() => HttpTransfer.RunAsync((token, progress) =>
        {
            for (int chunk = 0; chunk < 5; chunk++)
            {
                clock.Advance(TimeSpan.FromMinutes(1));
                token.ThrowIfCancellationRequested();
                progress();
            }
            clock.Advance(TimeSpan.FromSeconds(119));
            token.ThrowIfCancellationRequested();
            clock.Advance(TimeSpan.FromSeconds(1));
            return Task.FromResult(true);
        }, TestContext.Current.CancellationToken, clock));
        Assert.Contains("inactivity", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.All(clock.Timers, timer => Assert.True(timer.Disposed));
    }

    [Fact]
    public async Task RunAsync_WhenProgressContinues_AllowsMultiHourTransfers()
    {
        var clock = new ControlledTimeProvider();
        bool completed = await HttpTransfer.RunAsync((token, progress) =>
        {
            for (int chunk = 0; chunk < 180; chunk++)
            {
                clock.Advance(TimeSpan.FromMinutes(1));
                token.ThrowIfCancellationRequested();
                progress();
            }
            return Task.FromResult(true);
        }, TestContext.Current.CancellationToken, clock);
        Assert.True(completed);
        Assert.All(clock.Timers, timer => Assert.True(timer.Disposed));
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
