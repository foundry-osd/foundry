// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.WinPE.Diagnostics;
using Foundry.WinPE.Processes;
using Foundry.WinPE.SystemPreparation;
using Serilog;
using Xunit;

namespace Foundry.WinPE.Tests;

public sealed class BootstrapCoordinatorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EarlyClockFailureDoesNotPreventTheUiProcess(bool fails)
    {
        using var fixture = new Fixture { EarlyClockFails = fails };
        Assert.Equal(BootstrapOutcome.Succeeded, (await fixture.RunAsync()).Outcome);
        Assert.True(fixture.Calls.IndexOf("clock") > fixture.Calls.IndexOf("network"));
        Assert.True(fixture.Calls.IndexOf("clock") < fixture.Calls.IndexOf("ui"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(20)]
    [InlineData(7)]
    public async Task DiagnosticsObserveOneTerminalOutcomeAndDeliveryFollowsTheUiProcess(int exitCode)
    {
        using var fixture = new Fixture { UiExit = exitCode, ObserveDiagnostics = true };
        BootstrapResult result = await fixture.RunAsync();
        Assert.Equal(exitCode == 0 ? BootstrapOutcome.Succeeded : exitCode == 20 ? BootstrapOutcome.Cancelled : BootstrapOutcome.Failed, result.Outcome);
        Assert.Equal(exitCode == 0 ? 0 : exitCode == 20 ? 20 : 1, result.ExitCode);
        Assert.Single(fixture.Outcomes);
        Assert.Equal(result, fixture.Outcomes[0]);
        if (exitCode == 0) { Assert.True(fixture.Calls.IndexOf("ui") < fixture.Calls.IndexOf("delivery")); }
        else { Assert.DoesNotContain("delivery", fixture.Calls); }
    }

    [Theory]
    [InlineData(20, true)]
    [InlineData(21, false)]
    [InlineData(22, false)]
    [InlineData(7, false)]
    public async Task UiNonzeroExitIsReportedAndPersistsLogs(int exitCode, bool cancelled)
    {
        using var fixture = new Fixture { UiExit = exitCode };
        BootstrapResult result = await fixture.RunAsync();
        Assert.Equal(cancelled ? BootstrapOutcome.Cancelled : BootstrapOutcome.Failed, result.Outcome);
        Assert.Equal(BootstrapStage.Connect, result.Stage);
        Assert.Equal(exitCode, result.ChildExitCode);
        Assert.Equal(cancelled ? 20 : 1, result.ExitCode);
        Assert.Equal("persist", fixture.Calls[^1]);
    }

    [Fact]
    public async Task TheUiProcessLaunchesOnceFromTheGuardianExecutableWithoutResolvingAnyRuntime()
    {
        using var fixture = new Fixture();
        BootstrapResult result = await fixture.RunAsync();
        Assert.Equal(BootstrapOutcome.Succeeded, result.Outcome);
        Assert.Equal(["network", "clock", "ui", "persist"], fixture.Calls);
        Assert.Equal([GuardianPath], fixture.LaunchedExecutables);
        Assert.Equal("TEST", fixture.UiEnvironment!["FOUNDRY_DIAGNOSTIC_SESSION_ID"]);
        // Deploy reads PostInstall and the debug AutopilotInteractive archive from fixed boot image paths.
        Assert.DoesNotContain(fixture.UiEnvironment.Keys, key => key.StartsWith("FOUNDRY_POSTINSTALL", StringComparison.Ordinal));
        Assert.DoesNotContain(fixture.UiEnvironment.Keys, key => key.StartsWith("FOUNDRY_AUTOPILOTINTERACTIVE", StringComparison.Ordinal));
        Assert.DoesNotContain(fixture.Progress, item => item.Message.Contains("PostInstall", StringComparison.Ordinal));
        Assert.Equal([BootstrapStage.Environment, BootstrapStage.Connect], fixture.Progress.Select(item => item.Stage).Distinct());
    }

    [Fact]
    public async Task PreparationAndPersistenceFailuresDoNotReplaceASuccessfulRun()
    {
        using var fixture = new Fixture { PreparationFails = true, PersistenceFails = true };
        Assert.Equal(BootstrapOutcome.Succeeded, (await fixture.RunAsync()).Outcome);
        Assert.Contains(fixture.Progress, item => item.Status == BootstrapStatus.Warning);
        Assert.Contains("ui", fixture.Calls);
    }

    [Fact]
    public async Task CancellationWhileTheUiRunsStillPersists()
    {
        using var fixture = new Fixture { CancelUi = true };
        Assert.Equal(BootstrapOutcome.Cancelled, (await fixture.RunAsync()).Outcome);
        Assert.Equal("persist", fixture.Calls[^1]);
    }

    [Fact]
    public async Task UiStartFailureRetainsTheFailedStageEvenWhenPersistenceFails()
    {
        using var fixture = new Fixture { UiStartFails = true, PersistenceFails = true };
        BootstrapResult result = await fixture.RunAsync();
        Assert.Equal(BootstrapOutcome.Failed, result.Outcome);
        Assert.Equal(BootstrapStage.Connect, result.Stage);
        Assert.Equal(1, fixture.Calls.Count(item => item == "ui"));
    }

    [Fact]
    public async Task InternalTimeoutIsFailureRatherThanOperatorCancellation()
    {
        using var fixture = new Fixture { UiTimeout = true };
        BootstrapResult result = await fixture.RunAsync();
        Assert.Equal(BootstrapOutcome.Failed, result.Outcome);
    }

    [Fact]
    public async Task UiFailureCarriesTheGuardianPayloadVersion()
    {
        using var fixture = new Fixture { UiExit = 7, ReadPayloadVersion = ReadTestPayloadVersion };
        BootstrapResult result = await fixture.RunAsync();
        Assert.Equal(BootstrapOutcome.Failed, result.Outcome);
        Assert.Equal("26.11.16.1", result.PayloadVersion);
    }

    private const string GuardianPath = @"X:\Foundry\WinPE\Foundry.WinPE.exe";

    private static string? ReadTestPayloadVersion(string executable) => executable switch
    {
        GuardianPath => "26.11.16.1",
        _ => "1.0.0.0"
    };

    private sealed class Fixture : IDisposable, ISystemPreparation, IApplicationLauncher, IBootstrapLogPersistence
    {
        private readonly Serilog.Core.Logger logger = new LoggerConfiguration().CreateLogger();
        private readonly CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        internal BootstrapContext Context { get; set; } = new(@"X:\Foundry", "win-x64", "TEST", @"D:\Logs\TEST");
        internal List<string> Calls { get; } = [];
        internal List<string> LaunchedExecutables { get; } = [];
        internal List<BootstrapProgress> Progress { get; } = [];
        internal int UiExit { get; init; }
        internal bool PreparationFails { get; init; }
        internal bool PersistenceFails { get; init; }
        internal bool CancelUi { get; init; }
        internal bool UiStartFails { get; init; }
        internal IReadOnlyDictionary<string, string?>? UiEnvironment { get; private set; }
        internal bool UiTimeout { get; init; }
        internal bool ObserveDiagnostics { get; init; }
        internal bool EarlyClockFails { get; init; }
        internal Func<string, string?>? ReadPayloadVersion { get; init; }
        internal List<BootstrapResult> Outcomes { get; } = [];

        internal Task<BootstrapResult> RunAsync() => new BootstrapCoordinator(Context, this, this, this,
            logger, Progress.Add,
            ObserveDiagnostics ? () => Calls.Add("delivery") : null,
            ObserveDiagnostics ? (result, _) => Outcomes.Add(result) : null, ReadPayloadVersion, GuardianPath).RunAsync(cancellation.Token);

        public Task PrepareNetworkAsync(CancellationToken cancellationToken)
        {
            Calls.Add("network");
            return PreparationFails ? Task.FromException(new IOException()) : Task.CompletedTask;
        }

        public Task PrepareClockAsync(CancellationToken cancellationToken)
        {
            Calls.Add("clock");
            return EarlyClockFails ? Task.FromException(new HttpRequestException("Offline")) : Task.CompletedTask;
        }

        public Task PrepareSystemAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The UI process prepares the system, not the guardian.");

        public Task<ApplicationLaunchResult> RunUiAsync(string executable, IReadOnlyDictionary<string, string?> environment,
            CancellationToken cancellationToken)
        {
            Calls.Add("ui");
            LaunchedExecutables.Add(executable);
            UiEnvironment = environment;
            if (CancelUi) { cancellation.Cancel(); cancellationToken.ThrowIfCancellationRequested(); }
            if (UiTimeout) { throw new TaskCanceledException("Internal operation timed out."); }
            if (UiStartFails) { throw new IOException(); }
            return Task.FromResult(new ApplicationLaunchResult(UiExit == 0, UiExit, ReadinessConfirmed: true));
        }

        public Task PersistAsync(CancellationToken cancellationToken)
        {
            Calls.Add("persist");
            if (PersistenceFails) { throw new IOException(); }
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            logger.Dispose();
            cancellation.Dispose();
        }
    }
}
