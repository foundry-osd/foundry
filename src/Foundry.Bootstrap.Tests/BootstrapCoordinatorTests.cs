// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Bootstrap.Diagnostics;
using Foundry.Bootstrap.Processes;
using Foundry.Bootstrap.Runtime;
using Foundry.Bootstrap.SystemPreparation;
using Serilog;
using Xunit;

namespace Foundry.Bootstrap.Tests;

public sealed class BootstrapCoordinatorTests
{
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
        Assert.DoesNotContain("deploy", fixture.Calls);
        Assert.Equal("persist", fixture.Calls[^1]);
    }

    [Fact]
    public async Task UsbRefreshesConnectOnlyAfterNetworkProvisioningAndTimePreparation()
    {
        using var fixture = new Fixture();
        BootstrapResult result = await fixture.RunAsync();
        Assert.Equal(BootstrapOutcome.Succeeded, result.Outcome);
        Assert.Equal(["network", "Foundry.Connect:True", "connect", "system", "Foundry.Connect:False",
            "Foundry.Deploy:False", "deploy", "persist"], fixture.Calls);
    }

    [Fact]
    public async Task DebugAndIsoProvisioningNeverRefreshConnect()
    {
        using var fixture = new Fixture();
        fixture.Context = fixture.Context with { PersistenceDirectory = null, ConnectIsDebug = true, DeployIsDebug = true };
        Assert.Equal(BootstrapOutcome.Succeeded, (await fixture.RunAsync()).Outcome);
        Assert.DoesNotContain("Foundry.Connect:False", fixture.Calls);
        Assert.Contains("Foundry.Deploy:True", fixture.Calls);
    }

    [Fact]
    public async Task MissingConnectCacheMayResolveReleaseBeforeLaunching()
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

    private sealed class Fixture : IDisposable, IRuntimeResolver, ISystemPreparation, IApplicationLauncher, IBootstrapLogPersistence
    {
        private readonly Serilog.Core.Logger logger = new LoggerConfiguration().CreateLogger();
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
        internal bool ConnectTimeout { get; init; }

        internal Task<BootstrapResult> RunAsync() => new BootstrapCoordinator(Context, this, this, this, this,
            logger, Progress.Add).RunAsync(cancellation.Token);

        public Task<string> ResolveAsync(string applicationName, bool skipReleaseLookup, CancellationToken cancellationToken)
        {
            Calls.Add($"{applicationName}:{skipReleaseLookup}");
            if (MissingConnectCache && applicationName == "Foundry.Connect" && skipReleaseLookup)
            {
                throw new FileNotFoundException();
            }
            return Task.FromResult(applicationName + ".exe");
        }

        public Task PrepareNetworkAsync(CancellationToken cancellationToken) { Calls.Add("network"); return Task.CompletedTask; }
        public Task PrepareSystemAsync(CancellationToken cancellationToken)
        {
            Calls.Add("system");
            if (PreparationFails) { throw new IOException(); }
            return Task.CompletedTask;
        }

        public Task<int> RunConnectAsync(string executable, string configurationPath,
            IReadOnlyDictionary<string, string?> environment, CancellationToken cancellationToken)
        {
            Calls.Add("connect");
            Assert.Equal("TEST", environment["FOUNDRY_DIAGNOSTIC_SESSION_ID"]);
            if (CancelConnect) { cancellation.Cancel(); cancellationToken.ThrowIfCancellationRequested(); }
            if (ConnectTimeout) { throw new TaskCanceledException("Internal operation timed out."); }
            return Task.FromResult(ConnectExit);
        }

        public Task StartDeployAsync(string executable, IReadOnlyDictionary<string, string?> environment, CancellationToken cancellationToken)
        {
            Calls.Add("deploy");
            if (DeployFails) { throw new IOException(); }
            return Task.CompletedTask;
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
