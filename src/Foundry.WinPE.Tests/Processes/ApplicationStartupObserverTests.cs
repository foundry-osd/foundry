// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.WinPE.Processes;
using Foundry.Core.Models.Runtime;
using Xunit;

namespace Foundry.WinPE.Tests.Processes;

public sealed class ApplicationStartupObserverTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(20, false)]
    [InlineData(22, false)]
    public async Task ImmediateExitIsTheOutcomeWithoutReadiness(int exitCode, bool succeeded)
    {
        var process = new Child { Exited = true, ExitCode = exitCode };
        ApplicationLaunchResult result = await new ApplicationStartupObserver().ObserveAsync(process,
            () => null, TestContext.Current.CancellationToken);
        Assert.Equal(succeeded, result.Succeeded);
        Assert.Equal(exitCode, result.ExitCode);
        Assert.False(result.ReadinessConfirmed);
    }

    [Fact]
    public async Task ReadyUiProcessCanRunBeyondTheStartupDeadline()
    {
        var clock = new Clock();
        var process = new Child { OnWait = () => clock.Seconds += 600 };
        ApplicationLaunchResult result = await new ApplicationStartupObserver(clock).ObserveAsync(process,
            () => Status(StartupStage.UiReady), TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded);
        Assert.True(result.ReadinessConfirmed);
        Assert.Equal(600, clock.Seconds);
    }

    [Fact]
    public async Task DeadlineLeavesUnconfirmedChildRunning()
    {
        var clock = new Clock();
        var process = new Child();
        var observer = new ApplicationStartupObserver(clock, _ => { clock.Seconds += 121; return Task.CompletedTask; });
        ApplicationLaunchResult result = await observer.ObserveAsync(process,
            () => Status(StartupStage.ConfigurationLoaded), TestContext.Current.CancellationToken);
        Assert.False(result.Succeeded);
        Assert.Equal("readiness_timeout", result.FailureCategory);
        Assert.Equal(StartupStage.ConfigurationLoaded, result.LastStage);
        Assert.Null(result.ExitCode);
        Assert.False(process.HasExited);
    }

    [Fact]
    public async Task ReportedFailureStopsObservationWithoutInventingExitCode()
    {
        var process = new Child();
        ApplicationLaunchResult result = await new ApplicationStartupObserver().ObserveAsync(process,
            () => Status(StartupStage.StartupFailed), TestContext.Current.CancellationToken);
        Assert.False(result.Succeeded);
        Assert.Equal("startup_failed", result.FailureCategory);
        Assert.Null(result.ExitCode);
        Assert.False(process.HasExited);
    }

    [Fact]
    public async Task ReadyUiProcessReadsItsFinalStatusAtExit()
    {
        string stage = StartupStage.UiReady;
        var process = new Child { ExitCode = 22, OnWait = () => stage = StartupStage.StartupFailed };
        ApplicationLaunchResult result = await new ApplicationStartupObserver().ObserveAsync(process,
            () => Status(stage), TestContext.Current.CancellationToken);
        Assert.False(result.Succeeded);
        Assert.True(result.ReadinessConfirmed);
        Assert.Equal(StartupStage.StartupFailed, result.LastStage);
        Assert.Equal(22, result.ExitCode);
    }

    [Fact]
    public async Task CancellationDoesNotWaitForOrTerminateChild()
    {
        using var cancellation = new CancellationTokenSource();
        var process = new Child();
        var observer = new ApplicationStartupObserver(poll: _ => { cancellation.Cancel(); return Task.CompletedTask; });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => observer.ObserveAsync(process,
            () => null, cancellation.Token));
        Assert.False(process.HasExited);
    }

    private static RuntimeStartupStatus Status(string stage) => new() { Stage = stage };

    private sealed class Child : IObservedApplication
    {
        internal bool Exited { get; set; }
        internal Func<bool>? ProbeExit { get; init; }
        internal Action? OnWait { get; init; }
        public bool HasExited => ProbeExit?.Invoke() ?? Exited;
        public int ExitCode { get; init; }
        public Task WaitForExitAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OnWait?.Invoke();
            Exited = true;
            return Task.CompletedTask;
        }
    }

    private sealed class Clock : TimeProvider
    {
        internal long Seconds { get; set; }
        public override long GetTimestamp() => Seconds * TimestampFrequency;
    }
}
