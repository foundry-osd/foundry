// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.Concurrent;
using Foundry.Bootstrap.Diagnostics;
using Foundry.Bootstrap.Processes;
using Foundry.Bootstrap.Runtime;
using Foundry.Bootstrap.SystemPreparation;
using Foundry.Core.Services.Runtime;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace Foundry.Bootstrap.Tests;

public sealed class BootstrapCoordinatorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EarlyClockFailureDoesNotPreventConnectOrThePostConnectRetry(bool fails)
    {
        using var fixture = new Fixture { EarlyClockFails = fails };
        Assert.Equal(BootstrapOutcome.Succeeded, (await fixture.RunAsync()).Outcome);
        Assert.True(fixture.Calls.IndexOf("clock") > fixture.Calls.IndexOf("network"));
        Assert.True(fixture.Calls.IndexOf("clock") < fixture.Calls.IndexOf("connect"));
        Assert.True(fixture.Calls.IndexOf("system") > fixture.Calls.IndexOf("connect"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(20)]
    [InlineData(7)]
    public async Task DiagnosticsObserveOneTerminalOutcomeAndDeliveryFollowsSystemPreparation(int exitCode)
    {
        using var fixture = new Fixture { ConnectExit = exitCode, ObserveDiagnostics = true };
        BootstrapResult result = await fixture.RunAsync();
        Assert.Equal(exitCode == 0 ? BootstrapOutcome.Succeeded : exitCode == 20 ? BootstrapOutcome.Cancelled : BootstrapOutcome.Failed, result.Outcome);
        Assert.Equal(exitCode == 0 ? 0 : exitCode == 20 ? 20 : 1, result.ExitCode);
        Assert.Single(fixture.Outcomes);
        Assert.Equal(result, fixture.Outcomes[0]);
        if (exitCode == 0)
        {
            Assert.True(fixture.Calls.IndexOf("system") < fixture.Calls.IndexOf("delivery"));
            Assert.True(fixture.Calls.IndexOf("delivery") < fixture.Calls.IndexOf("deploy"));
        }
        else { Assert.DoesNotContain("delivery", fixture.Calls); }
    }

    [Theory]
    [InlineData(20, true)]
    [InlineData(21, false)]
    [InlineData(22, false)]
    [InlineData(7, false)]
    public async Task ConnectNonzeroExitPreventsDeployAndPersistsLogs(int exitCode, bool cancelled)
    {
        using var fixture = new Fixture { ConnectExit = exitCode };
        BootstrapResult result = await fixture.RunAsync();
        Assert.Equal(cancelled ? BootstrapOutcome.Cancelled : BootstrapOutcome.Failed, result.Outcome);
        Assert.Equal(exitCode, result.ChildExitCode);
        Assert.Equal(cancelled ? 20 : 1, result.ExitCode);
        Assert.DoesNotContain("deploy", fixture.Calls);
        Assert.Equal("persist", fixture.Calls[^1]);
    }

    [Fact]
    public async Task UsbRefreshesConnectOnlyAfterNetworkProvisioningAndTimePreparation()
    {
        using var fixture = new Fixture();
        BootstrapResult result = await fixture.RunAsync();
        Assert.Equal(BootstrapOutcome.Succeeded, result.Outcome);
        Assert.Equal(["network", "clock", "Foundry.Connect:False", "connect", "system", "Foundry.Connect:Refresh",
            "Foundry.Deploy:False", "Foundry.PostInstall:False", "deploy", "persist"], fixture.Calls);
        Assert.Equal("Foundry.PostInstall.exe", fixture.DeployEnvironment!["FOUNDRY_POSTINSTALL_PATH"]);
        Assert.Equal("TEST", fixture.DeployEnvironment["FOUNDRY_DIAGNOSTIC_SESSION_ID"]);
        Assert.False(fixture.Context.ChildEnvironment.ContainsKey("FOUNDRY_POSTINSTALL_PATH"));
    }

    [Fact]
    public async Task DebugAndIsoProvisioningNeverRefreshConnect()
    {
        using var fixture = new Fixture();
        fixture.Context = fixture.Context with { PersistenceDirectory = null, ConnectIsDebug = true, DeployIsDebug = true };
        Assert.Equal(BootstrapOutcome.Succeeded, (await fixture.RunAsync()).Outcome);
        Assert.DoesNotContain("Foundry.Connect:False", fixture.Calls);
        Assert.DoesNotContain("Foundry.Connect:Refresh", fixture.Calls);
        Assert.Contains("Foundry.Deploy:True", fixture.Calls);
        Assert.Contains("Foundry.PostInstall:True", fixture.Calls);
        Assert.DoesNotContain("Foundry.PostInstall:False", fixture.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PostInstallPreparationFailurePreventsDeploymentAndPersistsLogs(bool debug)
    {
        using var fixture = new Fixture { PostInstallFails = true };
        fixture.Context = fixture.Context with { DeployIsDebug = debug };

        BootstrapResult result = await fixture.RunAsync();

        Assert.Equal(BootstrapOutcome.Failed, result.Outcome);
        Assert.Equal(BootstrapStage.DeploymentPreparation, result.Stage);
        Assert.Contains($"Foundry.PostInstall:{debug}", fixture.Calls);
        Assert.DoesNotContain("deploy", fixture.Calls);
        Assert.Equal("persist", fixture.Calls[^1]);
    }

    [Fact]
    public async Task ConnectChecksForVerifiedUpdatesBeforeLaunching()
    {
        using var fixture = new Fixture { MissingConnectCache = true };
        Assert.Equal(BootstrapOutcome.Succeeded, (await fixture.RunAsync()).Outcome);
        Assert.True(fixture.Calls.IndexOf("Foundry.Connect:False") < fixture.Calls.IndexOf("connect"));
    }

    [Fact]
    public async Task MissingDebugConnectDoesNotFallBackToGithub()
    {
        using var fixture = new Fixture { MissingConnectCache = true };
        fixture.Context = fixture.Context with { ConnectIsDebug = true };
        Assert.Equal(BootstrapOutcome.Failed, (await fixture.RunAsync()).Outcome);
        Assert.DoesNotContain("Foundry.Connect:False", fixture.Calls);
        Assert.DoesNotContain("deploy", fixture.Calls);
    }

    [Fact]
    public async Task PreparationAndPersistenceFailuresDoNotReplaceSuccessfulHandoff()
    {
        using var fixture = new Fixture { PreparationFails = true, PersistenceFails = true };
        Assert.Equal(BootstrapOutcome.Succeeded, (await fixture.RunAsync()).Outcome);
        Assert.Contains(fixture.Progress, item => item.Status == BootstrapStatus.Warning);
        Assert.Contains("deploy", fixture.Calls);
    }

    [Fact]
    public async Task CancellationDuringConnectStopsSequenceAndStillPersists()
    {
        using var fixture = new Fixture { CancelConnect = true };
        Assert.Equal(BootstrapOutcome.Cancelled, (await fixture.RunAsync()).Outcome);
        Assert.DoesNotContain("system", fixture.Calls);
        Assert.DoesNotContain("deploy", fixture.Calls);
        Assert.Equal("persist", fixture.Calls[^1]);
    }

    [Fact]
    public async Task DeployStartFailureRetainsFailedStageEvenWhenPersistenceFails()
    {
        using var fixture = new Fixture { DeployFails = true, PersistenceFails = true };
        BootstrapResult result = await fixture.RunAsync();
        Assert.Equal(BootstrapOutcome.Failed, result.Outcome);
        Assert.Equal(BootstrapStage.Deploy, result.Stage);
        Assert.Equal(1, fixture.Calls.Count(item => item == "deploy"));
    }

    [Fact]
    public async Task InternalTimeoutIsFailureRatherThanOperatorCancellation()
    {
        using var fixture = new Fixture { ConnectTimeout = true };
        BootstrapResult result = await fixture.RunAsync();
        Assert.Equal(BootstrapOutcome.Failed, result.Outcome);
        Assert.DoesNotContain("deploy", fixture.Calls);
    }

    [Fact]
    public async Task ConnectFailureCarriesTheConnectPayloadVersion()
    {
        using var fixture = new Fixture { ConnectExit = 7, ReadPayloadVersion = ReadTestPayloadVersion };
        BootstrapResult result = await fixture.RunAsync();
        Assert.Equal(BootstrapOutcome.Failed, result.Outcome);
        Assert.Equal("26.10.18.1", result.PayloadVersion);
    }

    [Fact]
    public async Task DeployFailureCarriesTheDeployPayloadVersion()
    {
        using var fixture = new Fixture { DeployFails = true, ReadPayloadVersion = ReadTestPayloadVersion };
        BootstrapResult result = await fixture.RunAsync();
        Assert.Equal(BootstrapOutcome.Failed, result.Outcome);
        Assert.Equal(BootstrapStage.Deploy, result.Stage);
        Assert.Equal("26.10.20.3", result.PayloadVersion);
    }

    [Fact]
    public async Task ConnectResolutionFailureHasNoPayloadVersion()
    {
        using var fixture = new Fixture { ConnectResolveFails = true, ReadPayloadVersion = ReadTestPayloadVersion };
        BootstrapResult result = await fixture.RunAsync();
        Assert.Equal(BootstrapOutcome.Failed, result.Outcome);
        Assert.Equal(BootstrapStage.Connect, result.Stage);
        Assert.Null(result.PayloadVersion);
    }

    [Fact]
    public async Task DeployResolutionFailureHasNoPayloadVersion()
    {
        using var fixture = new Fixture { DeployResolveFails = true, ReadPayloadVersion = ReadTestPayloadVersion };
        BootstrapResult result = await fixture.RunAsync();
        Assert.Equal(BootstrapOutcome.Failed, result.Outcome);
        Assert.Equal(BootstrapStage.DeploymentPreparation, result.Stage);
        Assert.Null(result.PayloadVersion);
    }

    [Fact]
    public async Task ConnectUnlock_IsRelayedToDeployOnly()
    {
        using var fixture = new Fixture();

        Assert.Equal(BootstrapOutcome.Succeeded, (await fixture.RunAsync()).Outcome);

        Assert.Equal(SendPipe, fixture.ConnectEnvironment![DeploymentKeyHandoff.SendPipeEnvironmentVariable]);
        Assert.Null(fixture.ConnectEnvironment[DeploymentKeyHandoff.ReceivePipeEnvironmentVariable]);
        Assert.Equal(ReceivePipe, fixture.DeployEnvironment![DeploymentKeyHandoff.ReceivePipeEnvironmentVariable]);
        Assert.Null(fixture.DeployEnvironment[DeploymentKeyHandoff.SendPipeEnvironmentVariable]);
        Assert.Equal(["begin-receive", "sender:4242", "end-receive", "begin-serve", "receiver:4343", "end-serve", "dispose"], fixture.Relay.Calls);
        Assert.Equal(["delivered"], fixture.HandoffOutcomes());
        fixture.AssertNoEventContains(SendPipe);
        fixture.AssertNoEventContains(ReceivePipe);
    }

    [Fact]
    public async Task NoUnlock_DeployGetsNoPipe()
    {
        using var fixture = new Fixture { Relay = new FakeKeyRelay { ReceivesKey = false } };

        await fixture.RunAsync();

        Assert.Null(fixture.DeployEnvironment![DeploymentKeyHandoff.ReceivePipeEnvironmentVariable]);
        Assert.DoesNotContain("begin-serve", fixture.Relay.Calls);
        Assert.Equal(["not_requested"], fixture.HandoffOutcomes());
    }

    [Theory]
    [InlineData(20)]
    [InlineData(22)]
    public async Task ConnectFailure_DiscardsTheRelayBeforeDeploy(int exitCode)
    {
        using var fixture = new Fixture { ConnectExit = exitCode };

        await fixture.RunAsync();

        Assert.DoesNotContain("begin-serve", fixture.Relay.Calls);
        Assert.Equal("dispose", fixture.Relay.Calls[^1]);
    }

    [Fact]
    public async Task RelayFailure_NeverChangesTheBootOutcome()
    {
        using var fixture = new Fixture { Relay = new FakeKeyRelay { BeginReceiveThrows = true } };

        Assert.Equal(BootstrapOutcome.Succeeded, (await fixture.RunAsync()).Outcome);
        Assert.Null(fixture.ConnectEnvironment![DeploymentKeyHandoff.SendPipeEnvironmentVariable]);
        Assert.Null(fixture.DeployEnvironment![DeploymentKeyHandoff.ReceivePipeEnvironmentVariable]);
        Assert.Equal(["begin-receive", "dispose"], fixture.Relay.Calls);
        LogEvent failure = Assert.Single(fixture.Events, item => item.MessageTemplate.Text.StartsWith("Deployment key hand-off failed", StringComparison.Ordinal));
        Assert.Equal(LogEventLevel.Warning, failure.Level);
        Assert.Null(failure.Exception);
        Assert.Equal("\"IOException\"", failure.Properties["FailureType"].ToString());
        Assert.Equal(["not_requested"], fixture.HandoffOutcomes());
    }

    private const string SendPipe = "Foundry.DeploymentKey.00000000000000000000000000000001";
    private const string ReceivePipe = "Foundry.DeploymentKey.00000000000000000000000000000002";

    private static string? ReadTestPayloadVersion(string executable) => executable switch
    {
        "Foundry.Connect.exe" => "26.10.18.1",
        "Foundry.Deploy.exe" => "26.10.20.3",
        _ => "1.0.0.0"
    };

    private sealed class FakeKeyRelay : IDeploymentKeyRelay
    {
        internal bool ReceivesKey { get; init; } = true;
        internal bool BeginReceiveThrows { get; init; }
        internal List<string> Calls { get; } = [];
        public string BeginReceive()
        {
            Calls.Add("begin-receive");
            return BeginReceiveThrows ? throw new IOException() : SendPipe;
        }
        public void ExpectSender(int processId) => Calls.Add($"sender:{processId}");
        public Task<bool> EndReceiveAsync() { Calls.Add("end-receive"); return Task.FromResult(ReceivesKey); }
        public string? BeginServe() { Calls.Add("begin-serve"); return ReceivesKey ? ReceivePipe : null; }
        public void ExpectReceiver(int processId) => Calls.Add($"receiver:{processId}");
        public Task<bool> EndServeAsync() { Calls.Add("end-serve"); return Task.FromResult(true); }
        public void Dispose() => Calls.Add("dispose");
    }

    private sealed class DelegatingSink(Action<LogEvent> emit) : ILogEventSink
    {
        public void Emit(LogEvent logEvent) => emit(logEvent);
    }

    private sealed class Fixture : IDisposable, IRuntimeResolver, ISystemPreparation, IApplicationLauncher, IBootstrapLogPersistence
    {
        private readonly ConcurrentQueue<LogEvent> events = new();
        private readonly Serilog.Core.Logger logger;
        internal Fixture() => logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(new DelegatingSink(events.Enqueue)).CreateLogger();
        private readonly CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        internal BootstrapContext Context { get; set; } = new(@"X:\Foundry", @"D:\Runtime", "win-x64", "TEST", @"D:\Logs\TEST", false, false);
        internal List<string> Calls { get; } = [];
        internal List<BootstrapProgress> Progress { get; } = [];
        internal int ConnectExit { get; init; }
        internal bool MissingConnectCache { get; init; }
        internal bool PreparationFails { get; init; }
        internal bool PersistenceFails { get; init; }
        internal bool CancelConnect { get; init; }
        internal bool DeployFails { get; init; }
        internal bool PostInstallFails { get; init; }
        internal IReadOnlyDictionary<string, string?>? DeployEnvironment { get; private set; }
        internal bool ConnectTimeout { get; init; }
        internal bool ObserveDiagnostics { get; init; }
        internal bool EarlyClockFails { get; init; }
        internal bool ConnectResolveFails { get; init; }
        internal bool DeployResolveFails { get; init; }
        internal Func<string, string?>? ReadPayloadVersion { get; init; }
        internal List<BootstrapResult> Outcomes { get; } = [];

        internal FakeKeyRelay Relay { get; init; } = new();
        internal IReadOnlyDictionary<string, string?>? ConnectEnvironment { get; private set; }
        internal IReadOnlyList<LogEvent> Events => [.. events];

        internal Task<BootstrapResult> RunAsync() => new BootstrapCoordinator(Context, this, this, this, this,
            logger, Progress.Add,
            ObserveDiagnostics ? () => Calls.Add("delivery") : null,
            ObserveDiagnostics ? (result, _) => Outcomes.Add(result) : null, ReadPayloadVersion, () => Relay).RunAsync(cancellation.Token);

        internal List<string> HandoffOutcomes() =>
            [.. events.Where(item => item.Properties.ContainsKey("HandoffOutcome"))
                .Select(item => ((ScalarValue)item.Properties["HandoffOutcome"]).Value!.ToString()!)];

        // Neither the template, the rendered message nor any property may carry the text.
        internal void AssertNoEventContains(string text)
        {
            foreach (LogEvent logEvent in events)
            {
                Assert.DoesNotContain(text, logEvent.RenderMessage(), StringComparison.OrdinalIgnoreCase);
                Assert.All(logEvent.Properties.Values, value => Assert.DoesNotContain(text, value.ToString(), StringComparison.OrdinalIgnoreCase));
            }
        }

        public Task<string> ResolveAsync(string applicationName, bool skipReleaseLookup, CancellationToken cancellationToken)
        {
            Calls.Add($"{applicationName}:{skipReleaseLookup}");
            if (DeployResolveFails && applicationName == "Foundry.Deploy")
            {
                throw new InvalidDataException("No authenticated Deploy runtime is available.");
            }
            if (ConnectResolveFails && applicationName == "Foundry.Connect")
            {
                throw new InvalidDataException("No authenticated Connect runtime is available.");
            }
            if (PostInstallFails && applicationName == "Foundry.PostInstall")
            {
                throw new InvalidDataException("No authenticated PostInstall runtime is available.");
            }
            if (MissingConnectCache && applicationName == "Foundry.Connect" && skipReleaseLookup)
            {
                throw new FileNotFoundException();
            }
            return Task.FromResult(applicationName + ".exe");
        }

        public Task RefreshAsync(string applicationName, CancellationToken cancellationToken)
        {
            Calls.Add(applicationName + ":Refresh");
            return Task.CompletedTask;
        }

        public Task PrepareNetworkAsync(CancellationToken cancellationToken) { Calls.Add("network"); return Task.CompletedTask; }
        public Task PrepareClockAsync(CancellationToken cancellationToken)
        {
            Calls.Add("clock");
            return EarlyClockFails ? Task.FromException(new HttpRequestException("Offline")) : Task.CompletedTask;
        }
        public Task PrepareSystemAsync(CancellationToken cancellationToken)
        {
            Calls.Add("system");
            if (PreparationFails) { throw new IOException(); }
            return Task.CompletedTask;
        }

        public async Task<ApplicationLaunchResult> RunConnectAsync(string executable, string configurationPath,
            IReadOnlyDictionary<string, string?> environment, CancellationToken cancellationToken,
            Action<int>? processStarted = null, Func<Task>? processReleasing = null)
        {
            Calls.Add("connect");
            ConnectEnvironment = environment;
            Assert.Equal("TEST", environment["FOUNDRY_DIAGNOSTIC_SESSION_ID"]);
            if (CancelConnect) { cancellation.Cancel(); cancellationToken.ThrowIfCancellationRequested(); }
            if (ConnectTimeout) { throw new TaskCanceledException("Internal operation timed out."); }
            processStarted?.Invoke(4242);
            if (processReleasing is not null) { await processReleasing(); }
            return new ApplicationLaunchResult(ConnectExit == 0, ConnectExit);
        }

        public async Task<ApplicationLaunchResult> StartDeployAsync(string executable, IReadOnlyDictionary<string, string?> environment,
            CancellationToken cancellationToken, Action<int>? processStarted = null, Func<Task>? processReleasing = null)
        {
            Calls.Add("deploy");
            DeployEnvironment = environment;
            if (DeployFails) { throw new IOException(); }
            processStarted?.Invoke(4343);
            if (processReleasing is not null) { await processReleasing(); }
            return new ApplicationLaunchResult(true, ReadinessConfirmed: true);
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
