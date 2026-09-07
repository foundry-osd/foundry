// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Adk;
using Foundry.Core.Tests.TestUtilities;
using Foundry.Utilities.Processes;

namespace Foundry.Core.Tests.Adk;

public sealed class AdkInstallationServiceTests
{
    [Fact]
    public async Task InstallAsync_PreservesRebootWhenLaterStageFails()
    {
        using var fixture = new InstallFixture();
        var original = new InvalidOperationException("second stage failed");
        fixture.Runner.OnRun = (_, _) => fixture.Runner.Paths.Count == 1
            ? Task.FromResult(new AdkInstallerExecution(3010, true, false, false)) : throw original;
        Exception error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.InstallAsync(fixture.Workspace.Path, false, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Same(original, error);
        Assert.Equal(true, error.Data["AdkRebootRequired"]);
    }

    [Fact]
    public async Task InstallAsync_UncertainNativeCompletionRetainsBytesAndBlocksAnotherOperation()
    {
        using var fixture = new InstallFixture();
        fixture.Runner.OnRun = (_, _) => Task.FromResult(new AdkInstallerExecution(null, false, true, true));
        try
        {
            AdkInstallResult result = await fixture.Service.InstallAsync(fixture.Workspace.Path, false, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(AdkInstallOutcome.OwnershipUncertain, result.Outcome);
            Assert.True(fixture.Service.HasUncertainOwnership);
            Assert.Throws<IOException>(() => File.WriteAllText(fixture.AdkCache, "replacement"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.InstallAsync(fixture.Workspace.Path, false, cancellationToken: TestContext.Current.CancellationToken));
            Assert.Single(fixture.Runner.Paths);
        }
        finally
        {
            if (fixture.Service.RecoveryError?.Data[NativeFileLease.RetainedLeaseIdsDataKey] is Guid[] ids)
                foreach (Guid id in ids)
                    await NativeFileLease.ReconcileRetainedAsync(id, _ => Task.FromResult(true), CancellationToken.None);
        }
    }

    [Fact]
    public async Task InstallAsync_CancelledBeforeStartDoesNotDownloadOrLaunch()
    {
        using var fixture = new InstallFixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        AdkInstallResult result = await fixture.Service.InstallAsync(fixture.Workspace.Path, false, cancellationToken: cancellation.Token);
        Assert.Equal(AdkInstallOutcome.Cancelled, result.Outcome);
        Assert.Equal(0, fixture.Handler.Requests);
        Assert.Empty(fixture.Runner.Paths);
    }

    [Fact]
    public async Task InstallAsync_AcquiresAndValidatesBothBundlesBeforeUninstall()
    {
        using var fixture = new InstallFixture();
        fixture.Probe.Products =
        [
            new("Windows Assessment and Deployment Kit", "10.1.26100.2454", $"\"{fixture.OldAdk}\" /unsafe"),
            new("Windows PE Add-ons", "10.1.26100.2454", $"\"{fixture.OldWinPe}\" /unsafe")
        ];
        AdkInstallResult result = await fixture.Service.InstallAsync(fixture.Workspace.Path, true, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(AdkInstallOutcome.Ready, result.Outcome);
        Assert.Equal([fixture.OldWinPe, fixture.OldAdk, fixture.AdkCache, fixture.WinPeCache], fixture.Runner.Paths);
        Assert.All(fixture.Runner.Arguments.Take(2), args => Assert.Equal(["/uninstall", "/quiet", "/norestart"], args));
        Assert.Equal(2, fixture.Handler.Requests);
        Assert.True(fixture.Runner.BothPresentBeforeFirstRun);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InstallAsync_RejectsInvalidFreshOrCachedBytesBeforeNativeLaunch(bool cached)
    {
        using var fixture = new InstallFixture(reject: true);
        if (cached) File.WriteAllText(fixture.AdkCache, "corrupted");
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Service.InstallAsync(fixture.Workspace.Path, false, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Empty(fixture.Runner.Paths);
        if (cached) Assert.Equal("corrupted", File.ReadAllText(fixture.AdkCache));
    }

    [Fact]
    public async Task InstallAsync_RevalidatesCachedActualBytesAndAvoidsDownloadWhenValid()
    {
        using var fixture = new InstallFixture();
        File.WriteAllText(fixture.AdkCache, "verified");
        File.WriteAllText(fixture.WinPeCache, "verified");
        await fixture.Service.InstallAsync(fixture.Workspace.Path, false, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(0, fixture.Handler.Requests);
        Assert.True(fixture.Verifications.Count(path => path == fixture.AdkCache) >= 2);
        Assert.True(fixture.Verifications.Count(path => path == fixture.WinPeCache) >= 2);
    }

    [Fact]
    public async Task InstallAsync_CancellationAfterStartWaitsForCompletionAndStopsNextStage()
    {
        using var fixture = new InstallFixture();
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<AdkInstallerExecution>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Runner.OnRun = (_, _) => { started.SetResult(); return finish.Task; };
        Task<AdkInstallResult> operation = fixture.Service.InstallAsync(fixture.Workspace.Path, false, cancellationToken: cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        cancellation.Cancel();
        Assert.False(operation.IsCompleted);
        Assert.Throws<IOException>(() => File.WriteAllText(fixture.AdkCache, "replacement"));
        finish.SetResult(new(3010, true, true, false));
        AdkInstallResult result = await operation;
        Assert.Equal(AdkInstallOutcome.RebootRequired, result.Outcome);
        Assert.Single(fixture.Runner.Paths);
    }

    [Fact]
    public async Task InstallAsync_PreservesOriginalFailureIfFinalDetectionFails()
    {
        using var fixture = new InstallFixture();
        var original = new InvalidOperationException("native failure");
        fixture.Runner.OnRun = (_, _) => { fixture.Probe.Throw = true; throw original; };
        Exception error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.InstallAsync(fixture.Workspace.Path, false, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Same(original, error);
    }

    [Fact]
    public async Task InstallAsync_DoesNotDeclareReadyWhenFinalToolsAreMissing()
    {
        using var fixture = new InstallFixture();
        fixture.Probe.Ready = false;
        AdkInstallResult result = await fixture.Service.InstallAsync(fixture.Workspace.Path, false, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(AdkInstallOutcome.NotReady, result.Outcome);
    }

    [Theory]
    [InlineData(0, true, AdkInstallOutcome.Ready)]
    [InlineData(0, false, AdkInstallOutcome.NotReady)]
    [InlineData(3010, true, AdkInstallOutcome.RebootRequired)]
    [InlineData(3010, false, AdkInstallOutcome.RebootRequired)]
    [InlineData(1641, true, AdkInstallOutcome.RebootRequired)]
    [InlineData(1603, true, AdkInstallOutcome.NotReady)]
    public void Classify_RequiresReadinessAndPreservesReboot(int exitCode, bool ready, AdkInstallOutcome expected)
    {
        AdkInstallationStatus status = new(true, true, ready, "10.1.26100.2454", AdkVersionRelation.Supported, "C:/ADK", "Windows ADK 24H2 / 10.1.26100");
        AdkInstallResult result = AdkInstallResult.Classify(status, [exitCode, 0]);
        Assert.Equal(expected, result.Outcome);
        Assert.Equal(exitCode is 3010 or 1641, result.RebootRequired);
    }

    private sealed class InstallFixture : IDisposable
    {
        public TemporaryDirectory Workspace { get; } = new();
        public FakeProbe Probe { get; } = new();
        public FakeRunner Runner { get; } = new();
        public FakeHandler Handler { get; } = new();
        public List<string> Verifications { get; } = [];
        public AdkInstallationService Service { get; }
        public string AdkCache => Path.Combine(Workspace.Path, "adksetup-10.1.26100.2454.exe");
        public string WinPeCache => Path.Combine(Workspace.Path, "adkwinpesetup-10.1.26100.2454.exe");
        public string OldAdk => Path.Combine(Workspace.Path, "adksetup.exe");
        public string OldWinPe => Path.Combine(Workspace.Path, "adkwinpesetup.exe");
        private readonly HttpClient client;
        public InstallFixture(bool reject = false)
        {
            File.WriteAllText(OldAdk, "verified");
            File.WriteAllText(OldWinPe, "verified");
            client = new HttpClient(Handler);
            Runner.BothPresent = () => File.Exists(AdkCache) && File.Exists(WinPeCache);
            Service = new(Probe, Runner, client, (path, identity, token) =>
            {
                token.ThrowIfCancellationRequested();
                Verifications.Add(path);
                if (reject || File.ReadAllText(path) != "verified") throw new InvalidDataException("Untrusted installer bytes.");
                Assert.Throws<IOException>(() => File.Open(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite));
                return Task.CompletedTask;
            });
        }
        public void Dispose() { client.Dispose(); Workspace.Dispose(); }
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("verified") });
        }
    }

    private sealed class FakeRunner : IAdkInstallerRunner
    {
        public Task? ActiveOperation { get; private set; }
        public List<string> Paths { get; } = [];
        public List<IReadOnlyList<string>> Arguments { get; } = [];
        public Func<bool> BothPresent { get; set; } = () => false;
        public bool BothPresentBeforeFirstRun { get; private set; }
        public Func<string, CancellationToken, Task<AdkInstallerExecution>>? OnRun { get; set; }
        public Task<AdkInstallerExecution> RunAsync(string executablePath, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            if (Paths.Count == 0) BothPresentBeforeFirstRun = BothPresent();
            Paths.Add(executablePath);
            Arguments.Add(arguments);
            Task<AdkInstallerExecution> operation = OnRun?.Invoke(executablePath, cancellationToken) ?? Task.FromResult(new AdkInstallerExecution(0, true, false, false));
            ActiveOperation = operation;
            return operation;
        }
    }

    private sealed class FakeProbe : IAdkInstallationProbe
    {
        public bool Ready { get; set; } = true;
        public bool Throw { get; set; }
        public IReadOnlyList<AdkInstalledProduct> Products { get; set; } = [new("Windows Assessment and Deployment Kit", "10.1.26100.2454")];
        public string? GetKitsRootPath() => Throw ? throw new IOException("probe failure") : "C:/ADK";
        public bool DirectoryExists(string path) => Ready;
        public bool FileExists(string path) => Ready;
        public bool DirectoryContainsFile(string directoryPath, string fileName) => Ready;
        public IReadOnlyList<AdkInstalledProduct> GetInstalledProducts() => Products;
    }
}
