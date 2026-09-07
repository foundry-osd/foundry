// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.Media;
using Foundry.Core.Services.WinPe;
using Foundry.Core.Tests.TestUtilities;

namespace Foundry.Core.Tests.Media;

public sealed class MediaOperationCoordinatorTests
{
    [Fact]
    public async Task OsOnlySnapshotPreservesMediaCreationAndReportsMissingOptionalOem()
    {
        using var temporary = new TemporaryDirectory();
        var services = new FakeServices();
        services.ContinueBuild.SetResult();
        WinPeResult<MediaOperationResult> result = await services.CreateCoordinator().RunAsync(CreateRequest(temporary.Path),
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Warnings);
        Assert.NotNull(services.Preparation!.RuntimePayloadProvisioning);
        Assert.Single(services.Preparation.MediaManifest!.Applications);
        Assert.Equal("operating-systems", Assert.Single(services.Preparation.MediaManifest.CatalogSnapshots).Id);
    }

    [Fact]
    public async Task RunAsync_RejectsConcurrentRequestBeforeSecondBuild()
    {
        using var temporary = new TemporaryDirectory();
        var services = new FakeServices();
        MediaOperationCoordinator coordinator = services.CreateCoordinator();
        Task<WinPeResult<MediaOperationResult>> first = coordinator.RunAsync(CreateRequest(temporary.Path), cancellationToken: TestContext.Current.CancellationToken);
        await services.BuildEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
        MediaOperationRequest secondRequest = CreateRequest(temporary.Path);
        WinPeResult<MediaOperationResult> second = await coordinator.RunAsync(secondRequest, cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(second.IsSuccess);
        Assert.Equal(1, services.BuildCalls);
        Assert.All(secondRequest.Protection.DeploymentKey, value => Assert.Equal(0, value));
        Assert.True(coordinator.IsRunning);
        services.ContinueBuild.SetResult();
        Assert.True((await first).IsSuccess);
        Assert.False(coordinator.IsRunning);
    }

    [Fact]
    public async Task RunAsync_SourceListChangesDoNotReachCapturedRequest()
    {
        using var temporary = new TemporaryDirectory();
        var services = new FakeServices();
        MediaOperationCoordinator coordinator = services.CreateCoordinator();
        List<WinPeVendorSelection> vendors = [WinPeVendorSelection.Dell];
        List<AutopilotProfileSettings> profiles = [new()
        {
            Id = "captured", DisplayName = "Captured", FolderName = "Captured", Source = "test",
            ImportedAtUtc = DateTimeOffset.UnixEpoch, JsonContent = AutopilotOfflineProfileTestData.ValidJson
        }];
        var document = new FoundryConfigurationDocument { Autopilot = new() { Profiles = profiles } };
        MediaOperationRequest request = CreateRequest(temporary.Path, vendors, document);
        Task<WinPeResult<MediaOperationResult>> operation = coordinator.RunAsync(request, cancellationToken: TestContext.Current.CancellationToken);
        await services.BuildEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
        vendors.Clear();
        vendors.Add(WinPeVendorSelection.Hp);
        profiles.Clear();
        services.ContinueBuild.SetResult();
        Assert.True((await operation).IsSuccess);
        Assert.Equal(new[] { WinPeVendorSelection.Dell }, services.Preparation!.DriverVendors);
        Assert.Equal("captured", Assert.Single(services.ConnectDocument!.Autopilot.Profiles).Id);
    }

    [Fact]
    public async Task Cancellation_DoesNotReleaseOwnershipUntilOwnedCleanupCompletes()
    {
        using var temporary = new TemporaryDirectory();
        var services = new FakeServices { WaitForCancellationCleanup = true };
        MediaOperationCoordinator coordinator = services.CreateCoordinator();
        MediaOperationRequest request = CreateRequest(temporary.Path);
        Task<WinPeResult<MediaOperationResult>> operation = coordinator.RunAsync(request, cancellationToken: TestContext.Current.CancellationToken);
        await services.BuildEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
        Task<WinPeResult> closing = coordinator.RequestCancellationAndWaitAsync(TestContext.Current.CancellationToken);
        await services.CancellationObserved.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.False(closing.IsCompleted);
        Assert.True(coordinator.IsRunning);
        Assert.True(Directory.Exists(services.OperationWorkspace));
        Assert.Throws<IOException>(() => MediaOperationLease.Acquire(temporary.Path, "ADK"));
        Assert.Contains(request.Protection.DeploymentKey, value => value != 0);
        services.ContinueBuild.SetResult();
        Assert.False((await operation).IsSuccess);
        Assert.True((await closing).IsSuccess);
        Assert.False(Directory.Exists(services.OperationWorkspace));
        Assert.All(request.Protection.DeploymentKey, value => Assert.Equal(0, value));
    }

    [Fact]
    public async Task RunAsync_RecoveryPreservesWorkspaceAndPreparedOwnershipAndBlocksSecondRequest()
    {
        using var temporary = new TemporaryDirectory();
        var services = new FakeServices { RecoveryOnPublish = true };
        MediaOperationCoordinator coordinator = services.CreateCoordinator();
        try
        {
            services.ContinueBuild.SetResult();
            WinPeResult<MediaOperationResult> result = await coordinator.RunAsync(CreateRequest(temporary.Path), cancellationToken: TestContext.Current.CancellationToken);
            Assert.False(result.IsSuccess);
            Assert.True(result.Error!.RecoveryRequired);
            Assert.True(Directory.Exists(services.OperationWorkspace));
            Assert.False(services.Prepared!.IsDisposed);
            Assert.Single(Directory.GetFiles(temporary.Path, "*.operation.json"));
            Assert.Throws<IOException>(() => MediaOperationLease.Acquire(temporary.Path, "ADK"));
            Assert.False((await coordinator.RunAsync(CreateRequest(temporary.Path), cancellationToken: TestContext.Current.CancellationToken)).IsSuccess);
            Assert.Equal(1, services.BuildCalls);
            Assert.False((await coordinator.RequestCancellationAndWaitAsync(TestContext.Current.CancellationToken)).IsSuccess);
        }
        finally
        {
            services.Prepared?.Dispose();
            MediaOperationLeaseTests.ReleaseRetainedTestLeases(temporary.Path);
        }
    }

    [Fact]
    public async Task RunAsync_SuccessCleansOnlyOwnedWorkspaceAndPropagatesPublicationWarning()
    {
        using var temporary = new TemporaryDirectory();
        string unrelated = Path.Combine(temporary.Path, "prior-output");
        Directory.CreateDirectory(unrelated);
        await File.WriteAllTextAsync(Path.Combine(unrelated, "keep.iso"), "good", TestContext.Current.CancellationToken);
        var warning = new WinPeDiagnostic(WinPeErrorCodes.IsoCreateFailed, "Old output retained.") { RetainedPaths = [unrelated] };
        var services = new FakeServices { PublicationWarning = warning };
        services.ContinueBuild.SetResult();
        WinPeResult<MediaOperationResult> result = await services.CreateCoordinator().RunAsync(CreateRequest(temporary.Path), cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.Same(warning, result.CleanupDiagnostic);
        Assert.False(Directory.Exists(services.OperationWorkspace));
        Assert.True(services.Prepared!.IsDisposed);
        Assert.Empty(Directory.GetFiles(temporary.Path, "*.operation.json"));
        Assert.Equal("good", await File.ReadAllTextAsync(Path.Combine(unrelated, "keep.iso"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CloseJoin_BlocksNewAdmissionUntilCancelledCloseIsResumed()
    {
        using var temporary = new TemporaryDirectory();
        var services = new FakeServices();
        services.ContinueBuild.SetResult();
        MediaOperationCoordinator coordinator = services.CreateCoordinator();
        Assert.True((await coordinator.RunAsync(CreateRequest(temporary.Path), cancellationToken: TestContext.Current.CancellationToken)).IsSuccess);
        Assert.True((await coordinator.RequestCancellationAndWaitAsync(TestContext.Current.CancellationToken)).IsSuccess);
        Assert.False((await coordinator.RunAsync(CreateRequest(temporary.Path), cancellationToken: TestContext.Current.CancellationToken)).IsSuccess);
        Assert.Equal(1, services.BuildCalls);
        coordinator.ResumeAfterCancelledClose();
        Assert.True((await coordinator.RunAsync(CreateRequest(temporary.Path), cancellationToken: TestContext.Current.CancellationToken)).IsSuccess);
        Assert.Equal(2, services.BuildCalls);
    }
    private static MediaOperationRequest CreateRequest(string root, IReadOnlyList<WinPeVendorSelection>? vendors = null,
        FoundryConfigurationDocument? document = null)
    {
        document ??= new();
        return new MediaOperationRequest
        {
            Target = MediaOperationTarget.Iso,
            Options = new() { Architecture = WinPeArchitecture.X64, IsoOutputPath = Path.Combine(root, "output.iso"), DriverVendors = vendors ?? [] },
            Configuration = new(document, document, document, new OobeAccountSecretState()),
            Protection = new DeploymentMediaProtectionMaterial([1, 2, 3], new DeployProtectionSettings()),
            RuntimePayloads = new(),
            WorkspaceRoot = root,
            WinReCacheDirectoryPath = Path.Combine(root, "winre")
        };
    }

    private sealed class FakeServices : IWinPeBuildService, IWinPeWorkspacePreparationService,
        IWinPeRuntimePayloadProvisioningService, IWinPeIsoMediaService, IWinPeEmbeddedAssetService, IConnectConfigurationGenerator
    {
        public TaskCompletionSource BuildEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ContinueBuild { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CancellationObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool WaitForCancellationCleanup { get; init; }
        public bool RecoveryOnPublish { get; init; }
        public WinPeDiagnostic? PublicationWarning { get; init; }
        public int BuildCalls { get; private set; }
        public string OperationWorkspace { get; private set; } = string.Empty;
        public WinPeWorkspacePreparationOptions? Preparation { get; private set; }
        public FoundryConfigurationDocument? ConnectDocument { get; private set; }
        public WinPePreparedRuntimePayloads? Prepared { get; private set; }

        public MediaOperationCoordinator CreateCoordinator() => new(this, this, this, this, null!, this, this,
            new DeployConfigurationGenerator(), (_, _) => WinPeResult<WinPeToolPaths>.Success(new()), _ =>
            {
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes("<Catalog />");
                string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
                return Task.FromResult<IReadOnlyList<Foundry.Core.Services.Catalog.VerifiedCatalogDocument>>([
                    new("operating-systems", bytes, hash, "sha256:" + hash.ToLowerInvariant(), Foundry.Core.Services.Catalog.VerifiedCatalogSources.GetUri("operating-systems"), DateTimeOffset.UtcNow)]);
            });

        public async Task<WinPeResult<WinPeBuildArtifact>> BuildAsync(WinPeBuildOptions options, CancellationToken token)
        {
            BuildCalls++;
            OperationWorkspace = options.OutputDirectoryPath;
            using CancellationTokenRegistration registration = token.Register(() => CancellationObserved.TrySetResult());
            BuildEntered.TrySetResult();
            await ContinueBuild.Task.WaitAsync(TestContext.Current.CancellationToken);
            if (WaitForCancellationCleanup) token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(options.WorkingDirectoryPath!);
            return WinPeResult<WinPeBuildArtifact>.Success(new()
            {
                WorkingDirectoryPath = options.WorkingDirectoryPath!,
                MountDirectoryPath = Path.Combine(options.WorkingDirectoryPath!, "mount")
            });
        }

        public Task<WinPeResult<WinPeWorkspacePreparationResult>> PrepareAsync(WinPeWorkspacePreparationOptions options, CancellationToken token)
        {
            Preparation = options;
            return Task.FromResult(WinPeResult<WinPeWorkspacePreparationResult>.Success(new() { Artifact = options.Artifact!, Tools = options.Tools! }));
        }
        public Task<WinPeResult<WinPePreparedRuntimePayloads>> PrepareAsync(WinPeRuntimePayloadProvisioningOptions options,
            IProgress<WinPeDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            Prepared = new WinPePreparedRuntimePayloads(Guid.NewGuid(), [new("Foundry.Connect", "win-x64", options.WorkingDirectoryPath, WinPeProvisioningSource.Debug, null, null, [new("Foundry.Connect.exe", 1, new string('A', 64))])]);
            return Task.FromResult(WinPeResult<WinPePreparedRuntimePayloads>.Success(Prepared));
        }
        public Task<WinPeResult> ValidatePreparedAsync(WinPePreparedRuntimePayloads prepared, CancellationToken token = default) => throw new NotSupportedException();
        public Task<WinPeResult> ProvisionPreparedAsync(WinPePreparedRuntimePayloads prepared, WinPeRuntimePayloadProvisioningOptions options, CancellationToken token = default) => throw new NotSupportedException();
        public Task<WinPeResult> ProvisionAsync(WinPeRuntimePayloadProvisioningOptions options, IProgress<WinPeDownloadProgress>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WinPeResult> CreateAsync(WinPeIsoMediaOptions options, CancellationToken token)
        {
            return Task.FromResult(RecoveryOnPublish
                ? WinPeResult.Failure(new WinPeDiagnostic(WinPeErrorCodes.IsoCreateFailed, "Uncertain writer.") { RecoveryRequired = true })
                : WinPeResult.SuccessWithCleanup(PublicationWarning));
        }
        public FoundryConnectProvisioningBundle CreateProvisioningBundle(FoundryConfigurationDocument document, string path)
        {
            ConnectDocument = document;
            return new();
        }
        public FoundryConnectConfigurationDocument Generate(FoundryConfigurationDocument document, string path) => throw new NotSupportedException();
        public string Serialize(FoundryConnectConfigurationDocument document) => throw new NotSupportedException();
        public string GetBootstrapScriptContent() => "bootstrap";
        public string GetUsbProvisioningScriptTemplateContent() => throw new NotSupportedException();
        public string GetIanaWindowsTimeZoneMapJson() => "{}";
        public string GetSevenZipSourceDirectoryPath() => "fake-assets";
    }
}
