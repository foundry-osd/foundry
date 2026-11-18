// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Runtime;
using Foundry.WinPE.Diagnostics;
using Foundry.WinPE.Processes;
using Foundry.WinPE.SystemPreparation;
using Foundry.WinPE.Ui;
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

    [Fact]
    public async Task TheEnvironmentStageRunsBeforeTheUiProcessWhichRunsUntilItExits()
    {
        using var fixture = new Fixture();
        BootstrapResult result = await fixture.RunAsync();
        Assert.Equal(BootstrapOutcome.Succeeded, result.Outcome);
        Assert.Equal(0, result.ExitCode);
        // No system stage: the UI process prepares the clock and the time zone between its phases.
        Assert.Equal(["network", "clock", "ui", "persist"], fixture.Calls);
        Assert.Equal([GuardianPath], fixture.LaunchedExecutables);
        Assert.Equal("TEST", fixture.UiEnvironments[0]["FOUNDRY_DIAGNOSTIC_SESSION_ID"]);
        // Deploy reads PostInstall and the debug AutopilotInteractive archive from fixed boot image paths.
        Assert.DoesNotContain(fixture.UiEnvironments[0].Keys, key => key.StartsWith("FOUNDRY_POSTINSTALL", StringComparison.Ordinal));
        Assert.DoesNotContain(fixture.UiEnvironments[0].Keys, key => key.StartsWith("FOUNDRY_AUTOPILOTINTERACTIVE", StringComparison.Ordinal));
        Assert.DoesNotContain(fixture.Progress, item => item.Message.Contains("PostInstall", StringComparison.Ordinal));
        Assert.DoesNotContain(fixture.Progress, item => item.Stage is BootstrapStage.System or BootstrapStage.Deploy);
        Assert.DoesNotContain(fixture.Progress, item => item.Message.Contains("Foundry Deploy", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(20)]
    [InlineData(21)]
    [InlineData(30)]
    public async Task DeliveryStartsAfterTheUiProcessWithTheClockEvaluatedAtThatMoment(int exitCode)
    {
        using var fixture = new Fixture { ObserveDiagnostics = true };
        fixture.Launches.Enqueue(Exited(exitCode, ready: true, new UiState(UiPhase.Connect, false)));
        await fixture.RunAsync();
        int ui = fixture.Calls.IndexOf("ui");
        int delivery = fixture.Calls.IndexOf("delivery");
        Assert.True(delivery > ui);
        Assert.Equal("clock", fixture.Calls[delivery - 1]);
        Assert.Single(fixture.Calls, call => call == "delivery");
    }

    [Fact]
    public async Task ExitZeroSucceedsWithoutAFailureOrACrashWindow()
    {
        using var fixture = new Fixture { ObserveDiagnostics = true };
        BootstrapResult result = await fixture.RunAsync();
        Assert.Equal(BootstrapOutcome.Succeeded, result.Outcome);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(BootstrapOutcome.Succeeded, Assert.Single(fixture.Outcomes).Outcome);
        Assert.Empty(fixture.CrashWindows);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExitTwentyIsACancellationBeforeOrAfterReadiness(bool ready)
    {
        using var fixture = new Fixture { ObserveDiagnostics = true };
        fixture.Launches.Enqueue(Exited(20, ready, new UiState(UiPhase.Connect, false)));
        BootstrapResult result = await fixture.RunAsync();
        Assert.Equal(BootstrapOutcome.Cancelled, result.Outcome);
        Assert.Equal(20, result.ExitCode);
        Assert.Equal(BootstrapStatus.Cancelled, fixture.Progress[^1].Status);
        Assert.Empty(fixture.CrashWindows);
    }

    [Theory]
    [InlineData(21, false)]
    [InlineData(22, false)]
    [InlineData(23, false)]
    [InlineData(21, true)]
    [InlineData(22, true)]
    [InlineData(23, true)]
    public async Task ConnectFailureCodesAreReportedAsTodayBeforeOrAfterReadiness(int exitCode, bool ready)
    {
        using var fixture = new Fixture { ObserveDiagnostics = true };
        fixture.Launches.Enqueue(Exited(exitCode, ready, new UiState(UiPhase.Connect, false)));
        BootstrapResult result = await fixture.RunAsync();
        Assert.Equal(BootstrapOutcome.Failed, result.Outcome);
        Assert.Equal(1, result.ExitCode);
        Assert.Equal(BootstrapStage.Connect, result.Stage);
        Assert.Equal(exitCode, result.ChildExitCode);
        Assert.Equal("child_exit", result.FailureCategory);
        Assert.Equal(result, Assert.Single(fixture.Outcomes));
        Assert.Equal(ChildExitMessages.Describe(exitCode), fixture.Progress[^1].Message);
        Assert.Empty(fixture.CrashWindows);
        Assert.Equal("persist", fixture.Calls[^1]);
    }

    [Theory]
    [InlineData("startup_failed")]
    [InlineData("readiness_timeout")]
    [InlineData("capability_invalid")]
    public async Task StartupFailuresBeforeReadinessAreUnchanged(string category)
    {
        using var fixture = new Fixture { ObserveDiagnostics = true };
        fixture.Launches.Enqueue(new ApplicationLaunchResult(false, FailureCategory: category,
            LastStage: category == "startup_failed" ? StartupStage.StartupFailed : null));
        BootstrapResult result = await fixture.RunAsync();
        Assert.Equal(BootstrapOutcome.Failed, result.Outcome);
        Assert.Equal(1, result.ExitCode);
        Assert.Equal(BootstrapStage.Connect, result.Stage);
        Assert.Equal(category, result.FailureCategory);
        Assert.Equal(result, Assert.Single(fixture.Outcomes));
        Assert.Empty(fixture.CrashWindows);
    }

    [Fact]
    public async Task UnhandledExceptionBeforeReadinessIsAStartupFailureWithoutACrashWindow()
    {
        using var fixture = new Fixture { ObserveDiagnostics = true };
        fixture.Launches.Enqueue(Exited(30, ready: false, new UiState(UiPhase.Connect, false)));
        BootstrapResult result = await fixture.RunAsync();
        Assert.Equal(BootstrapOutcome.Failed, result.Outcome);
        Assert.Equal(BootstrapStage.Connect, result.Stage);
        Assert.Equal(30, result.ChildExitCode);
        Assert.Equal("child_exit", result.FailureCategory);
        Assert.Empty(fixture.CrashWindows);
        Assert.Equal(1, fixture.Calls.Count(call => call == "ui"));
    }

    [Fact]
    public async Task CrashBeforeTheDestructiveStepOffersRelaunchWhichStartsANewLaunch()
    {
        using var fixture = new Fixture { ObserveDiagnostics = true };
        fixture.Launches.Enqueue(Exited(30, ready: true, new UiState(UiPhase.Deploy, DestructiveStarted: false)));
        fixture.Launches.Enqueue(Exited(0, ready: true, new UiState(UiPhase.Deploy, DestructiveStarted: false)));
        fixture.Choices.Enqueue(UiCrashChoice.Relaunch);

        BootstrapResult result = await fixture.RunAsync();

        Assert.Equal([true], fixture.CrashWindows);
        BootstrapResult crash = Assert.Single(fixture.Outcomes);
        Assert.Equal(BootstrapOutcome.Failed, crash.Outcome);
        Assert.Equal(BootstrapStage.Deploy, crash.Stage);
        Assert.Equal(30, crash.ChildExitCode);
        Assert.Equal("child_exit", crash.FailureCategory);
        // The failure is sent and the logs persisted before the operator answers the window.
        Assert.Equal(["ui", "clock", "delivery", "persist", "crash", "ui"],
            fixture.Calls.SkipWhile(call => call != "ui").Take(6));
        Assert.Equal(2, fixture.LaunchedExecutables.Count);
        Assert.Equal(BootstrapOutcome.Succeeded, result.Outcome);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task CrashAfterTheDestructiveStepRefusesRelaunchAndCloseFailsTheBoot()
    {
        using var fixture = new Fixture { ObserveDiagnostics = true };
        fixture.Launches.Enqueue(Exited(30, ready: true, new UiState(UiPhase.Deploy, DestructiveStarted: true)));
        // Even a Relaunch answer cannot start another launch once the disk may have changed.
        fixture.Choices.Enqueue(UiCrashChoice.Relaunch);

        BootstrapResult result = await fixture.RunAsync();

        Assert.Equal([false], fixture.CrashWindows);
        Assert.Equal(1, fixture.Calls.Count(call => call == "ui"));
        Assert.Equal(BootstrapOutcome.Failed, result.Outcome);
        Assert.Equal(1, result.ExitCode);
        Assert.Equal(BootstrapStage.Deploy, result.Stage);
        Assert.Equal(result, Assert.Single(fixture.Outcomes));
        Assert.Equal(BootstrapStatus.Failed, fixture.Progress[^1].Status);
    }

    [Theory]
    [InlineData((int)UiPhase.Connect, (int)BootstrapStage.Connect)]
    [InlineData((int)UiPhase.System, (int)BootstrapStage.System)]
    [InlineData((int)UiPhase.Deploy, (int)BootstrapStage.Deploy)]
    public async Task CrashIsAttributedToThePhaseOfTheUiStateFile(int phase, int expected)
    {
        using var fixture = new Fixture { ObserveDiagnostics = true };
        fixture.Launches.Enqueue(Exited(-1, ready: true, new UiState((UiPhase)phase, DestructiveStarted: false)));
        fixture.Choices.Enqueue(UiCrashChoice.Close);

        BootstrapResult result = await fixture.RunAsync();

        Assert.Equal((BootstrapStage)expected, result.Stage);
        Assert.Equal(-1, result.ChildExitCode);
        Assert.Equal([true], fixture.CrashWindows);
    }

    [Fact]
    public async Task CrashWithAnUnknownUiStateIsAttributedToConnectAndNeverOffersRelaunch()
    {
        using var fixture = new Fixture { ObserveDiagnostics = true };
        fixture.Launches.Enqueue(Exited(1, ready: true, state: null));
        fixture.Choices.Enqueue(UiCrashChoice.Close);

        BootstrapResult result = await fixture.RunAsync();

        Assert.Equal([false], fixture.CrashWindows);
        Assert.Equal(BootstrapStage.Connect, result.Stage);
        Assert.Equal(1, result.ExitCode);
    }

    [Fact]
    public async Task OnlyTheFirstFailureIsReportedAcrossRelaunches()
    {
        using var fixture = new Fixture { ObserveDiagnostics = true };
        fixture.Launches.Enqueue(Exited(30, ready: true, new UiState(UiPhase.Connect, false)));
        fixture.Launches.Enqueue(Exited(30, ready: true, new UiState(UiPhase.Deploy, false)));
        fixture.Choices.Enqueue(UiCrashChoice.Relaunch);
        fixture.Choices.Enqueue(UiCrashChoice.Close);

        BootstrapResult result = await fixture.RunAsync();

        Assert.Equal([true, true], fixture.CrashWindows);
        Assert.Equal(BootstrapStage.Connect, Assert.Single(fixture.Outcomes).Stage);
        Assert.Equal(BootstrapStage.Deploy, result.Stage);
        Assert.Equal(1, result.ExitCode);
    }

    [Fact]
    public async Task CrashWindowFailureClosesTheBoot()
    {
        using var fixture = new Fixture { CrashWindowFails = true };
        fixture.Launches.Enqueue(Exited(30, ready: true, new UiState(UiPhase.Deploy, false)));

        BootstrapResult result = await fixture.RunAsync();

        Assert.Equal(BootstrapOutcome.Failed, result.Outcome);
        Assert.Equal(BootstrapStage.Deploy, result.Stage);
        Assert.Equal(1, fixture.Calls.Count(call => call == "ui"));
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
        using var fixture = new Fixture { ReadPayloadVersion = ReadTestPayloadVersion };
        fixture.Launches.Enqueue(Exited(21, ready: false, new UiState(UiPhase.Connect, false)));
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

    private static ApplicationLaunchResult Exited(int exitCode, bool ready, UiState? state) =>
        new(exitCode == 0, exitCode, ready, exitCode == 0 ? null : "child_exit", ready ? StartupStage.UiReady : null, state);

    private sealed class Fixture : IDisposable, ISystemPreparation, IApplicationLauncher, IBootstrapLogPersistence, IUiCrashScreen
    {
        private readonly Serilog.Core.Logger logger = new LoggerConfiguration().CreateLogger();
        private readonly CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        internal BootstrapContext Context { get; set; } = new(@"X:\Foundry", "win-x64", "TEST", @"D:\Logs\TEST");
        internal List<string> Calls { get; } = [];
        internal List<string> LaunchedExecutables { get; } = [];
        internal List<BootstrapProgress> Progress { get; } = [];
        internal Queue<ApplicationLaunchResult> Launches { get; } = [];
        internal Queue<UiCrashChoice> Choices { get; } = [];
        internal List<bool> CrashWindows { get; } = [];
        internal bool PreparationFails { get; init; }
        internal bool PersistenceFails { get; init; }
        internal bool CancelUi { get; init; }
        internal bool UiStartFails { get; init; }
        internal bool CrashWindowFails { get; init; }
        internal List<IReadOnlyDictionary<string, string?>> UiEnvironments { get; } = [];
        internal bool UiTimeout { get; init; }
        internal bool ObserveDiagnostics { get; init; }
        internal bool EarlyClockFails { get; init; }
        internal Func<string, string?>? ReadPayloadVersion { get; init; }
        internal List<BootstrapResult> Outcomes { get; } = [];

        internal Task<BootstrapResult> RunAsync() => new BootstrapCoordinator(Context, this, this, this, this,
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
            UiEnvironments.Add(environment);
            if (CancelUi) { cancellation.Cancel(); cancellationToken.ThrowIfCancellationRequested(); }
            if (UiTimeout) { throw new TaskCanceledException("Internal operation timed out."); }
            if (UiStartFails) { throw new IOException(); }
            return Task.FromResult(Launches.TryDequeue(out ApplicationLaunchResult? result)
                ? result
                : Exited(0, ready: true, new UiState(UiPhase.Deploy, false)));
        }

        public Task<UiCrashChoice> ShowAsync(bool relaunchAllowed, CancellationToken cancellationToken)
        {
            Calls.Add("crash");
            CrashWindows.Add(relaunchAllowed);
            if (CrashWindowFails) { throw new InvalidOperationException("No window station."); }
            return Task.FromResult(Choices.TryDequeue(out UiCrashChoice choice) ? choice : UiCrashChoice.Close);
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
