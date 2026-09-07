// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.Deployment.Unattend;
using Foundry.Deploy.Services.System;
using Foundry.Utilities.Processes;

namespace Foundry.Deploy.Tests;

public sealed class OfflineRegistryCleanupTests
{
    [Fact]
    public async Task ActionAndUnloadFailure_PreservesPrimaryAndBlocksFurtherMutation()
    {
        var runner = new HiveRunner { UnloadFails = true };
        var writer = new OfflineRegistryWriter(runner);
        var primary = new InvalidOperationException("primary");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.WithLoadedHiveAsync(@"HKLM\Foundry", "owned-hive", "owned-work", (_, _) => throw primary, TestContext.Current.CancellationToken));
        Assert.Same(primary, error);
        Assert.NotNull(error.Data["FoundryCleanupFailure"]);
        Assert.IsType<RecoveryResourceDiagnostic>(error.Data["FoundryRecoveryDiagnostic"]);
        int calls = runner.Calls.Count;
        await Assert.ThrowsAnyAsync<Exception>(() => writer.WithLoadedHiveAsync(@"HKLM\Foundry", "owned-hive", "owned-work", (_, _) => Task.CompletedTask, TestContext.Current.CancellationToken));
        Assert.Equal(calls, runner.Calls.Count);
    }

    [Fact]
    public async Task CancelledAction_CleanupUsesIndependentBoundedToken()
    {
        var runner = new HiveRunner();
        var writer = new OfflineRegistryWriter(runner);
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writer.WithLoadedHiveAsync(@"HKLM\Foundry", "owned-hive", "owned-work", (_, _) =>
        { cancellation.Cancel(); cancellation.Token.ThrowIfCancellationRequested(); return Task.CompletedTask; }, cancellation.Token));
        Assert.True(runner.UnloadTokenCanCancel);
        Assert.False(runner.UnloadTokenWasCancelled);
        Assert.False(runner.Loaded);
    }

    [Fact]
    public async Task PartialLoadFailure_ReconcilesAndUnloadsExactOwnedMount()
    {
        var runner = new HiveRunner { LoadFailsAfterMount = true };
        var writer = new OfflineRegistryWriter(runner);
        bool invoked = false;
        await Assert.ThrowsAsync<DeploymentProcessException>(() => writer.WithLoadedHiveAsync(@"HKLM\Foundry", "owned-hive", "owned-work", (_, _) => { invoked = true; return Task.CompletedTask; }, TestContext.Current.CancellationToken));
        Assert.False(invoked);
        Assert.False(runner.Loaded);
        Assert.Single(runner.Calls, c => c[0] == "UNLOAD");
    }

    [Fact]
    public async Task SuccessfulOperations_UseDifferentOwnedNamesAndConfirmAbsence()
    {
        var runner = new HiveRunner();
        var writer = new OfflineRegistryWriter(runner);
        var names = new List<string>();
        for (int i = 0; i < 2; i++)
            await writer.WithLoadedHiveAsync(@"HKLM\Foundry", "owned-hive", "owned-work", (hive, _) => { names.Add(hive.MountName); return Task.CompletedTask; }, TestContext.Current.CancellationToken);
        Assert.NotEqual(names[0], names[1]);
        Assert.All(names, name => Assert.StartsWith(@"HKLM\Foundry_", name));
        Assert.True(runner.ProbeCount >= 4);
    }

    [Fact]
    public async Task UncertainNativeLoad_DoesNotRaceCleanupAgainstRunningProcess()
    {
        var runner = new HiveRunner { UncertainLoad = true };
        var writer = new OfflineRegistryWriter(runner);
        var error = await Assert.ThrowsAsync<TimeoutException>(() => writer.WithLoadedHiveAsync(@"HKLM\Foundry", "owned-hive", "owned-work", (_, _) => Task.CompletedTask, TestContext.Current.CancellationToken));
        Assert.IsType<RecoveryResourceDiagnostic>(error.Data["FoundryRecoveryDiagnostic"]);
        Assert.DoesNotContain(runner.Calls, c => c[0] == "UNLOAD");
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Cleanup_UnknownStateOrTimeoutRetainsDiagnostic(bool unknown, bool timeout)
    {
        var runner = new HiveRunner { UnknownCleanupProbe = unknown, UnloadTimesOut = timeout };
        var writer = new OfflineRegistryWriter(runner);
        var error = await Assert.ThrowsAnyAsync<Exception>(() => writer.WithLoadedHiveAsync(@"HKLM\Foundry", "owned-hive", "owned-work", (_, _) => Task.CompletedTask, TestContext.Current.CancellationToken));
        Assert.IsType<RecoveryResourceDiagnostic>(error.Data["FoundryRecoveryDiagnostic"]);
        Assert.True(runner.Loaded);
    }

    [Fact]
    public async Task CancellationBeforeLoad_DoesNotStartDiscoveryOrMutation()
    {
        var runner = new HiveRunner();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new OfflineRegistryWriter(runner).WithLoadedHiveAsync(@"HKLM\Foundry", "owned-hive", "owned-work", (_, _) => Task.CompletedTask, cancellation.Token));
        Assert.Empty(runner.Calls);
    }

    internal sealed class HiveRunner : IProcessRunner
    {
        public bool Loaded { get; private set; }
        public bool UnloadFails { get; init; }
        public bool UnknownCleanupProbe { get; init; }
        public bool UnloadTimesOut { get; init; }
        public bool LoadFailsAfterMount { get; init; }
        public bool UncertainLoad { get; init; }
        public bool UnloadTokenCanCancel { get; private set; }
        public bool UnloadTokenWasCancelled { get; private set; }
        public int ProbeCount { get; private set; }
        public List<string[]> Calls { get; } = [];
        public Task<ProcessExecutionResult> RunAsync(string fileName, string arguments, string workingDirectory, CancellationToken cancellationToken = default, TimeSpan? executionTimeout = null) => throw new NotSupportedException();
        public Task<ProcessExecutionResult> RunAsync(string fileName, IEnumerable<string> arguments, string workingDirectory, CancellationToken cancellationToken = default, TimeSpan? executionTimeout = null)
        {
            var args = arguments.ToArray(); Calls.Add(args);
            if (fileName.EndsWith("powershell.exe", StringComparison.OrdinalIgnoreCase))
            { ProbeCount++; return Task.FromResult(new ProcessExecutionResult { ExitCode = 0, StandardOutput = UnknownCleanupProbe && Loaded ? "unknown" : Loaded ? "FOUNDRY_HIVE_PRESENT" : "FOUNDRY_HIVE_ABSENT" }); }
            if (args[0] == "LOAD")
            {
                Loaded = true;
                if (UncertainLoad) { var error = new TimeoutException(); error.Data["ProcessRootExitConfirmed"] = false; throw error; }
                if (LoadFailsAfterMount) { return Task.FromResult(new ProcessExecutionResult { ExitCode = 5 }); }
            }
            if (args[0] == "UNLOAD")
            {
                UnloadTokenCanCancel = cancellationToken.CanBeCanceled; UnloadTokenWasCancelled = cancellationToken.IsCancellationRequested;
                if (UnloadTimesOut) throw new TimeoutException("cleanup_timeout");
                if (UnloadFails) { return Task.FromResult(new ProcessExecutionResult { ExitCode = 5 }); }
                Loaded = false;
            }
            return Task.FromResult(new ProcessExecutionResult { ExitCode = 0 });
        }
        public Task<ProcessExecutionResult> RunAsync(string fileName, IEnumerable<string> arguments, string workingDirectory, Action<string>? onOutputData, Action<string>? onErrorData, CancellationToken cancellationToken = default, TimeSpan? executionTimeout = null)
            => RunAsync(fileName, arguments, workingDirectory, cancellationToken, executionTimeout);
    }
}
