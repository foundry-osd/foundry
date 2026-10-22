// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Core.Services.Drivers;
using Foundry.Core.Services.Packages;
using Foundry.Core.Services.WinPe;
using Foundry.Deploy.Models;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.Deployment.Steps;
using Foundry.Deploy.Services.DriverPacks;
using Foundry.Deploy.Services.Drivers;
using Foundry.Deploy.Services.Hardware;
using Foundry.Telemetry;
using Foundry.Utilities.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class CustomDriverSetStagingTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "Fdriversstaging", Guid.NewGuid().ToString("N"));
    private CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task StagingCopiesTheTreeAndReturnsTheHashFolder()
    {
        CustomDriverSetMedia media = await CustomDriverSetMedia.PublishAsync(Path.Combine(root, "media"), Cancellation);
        string destinationRoot = Path.Combine(root, "target", "Custom");

        string staged = await new CustomDriverSetStagingService().StageAsync(media.Set, destinationRoot, null, Cancellation);

        Assert.Equal(Path.Combine(destinationRoot, media.Set.ContentHash), staged);
        Assert.Equal("[Version]\r\nnet", await File.ReadAllTextAsync(Path.Combine(staged, "net.inf"), Cancellation));
        Assert.Equal("driver net", await File.ReadAllTextAsync(Path.Combine(staged, "net.sys"), Cancellation));
        Assert.Equal("[Version]\r\nwifi", await File.ReadAllTextAsync(Path.Combine(staged, "wifi", "wifi.inf"), Cancellation));
        Assert.Equal(media.Set.FileCount, Directory.EnumerateFiles(staged, "*", SearchOption.AllDirectories).Count());
    }

    [Fact]
    public async Task StagingFailsClosedOnHashMismatch()
    {
        CustomDriverSetMedia media = await CustomDriverSetMedia.PublishAsync(Path.Combine(root, "media"), Cancellation);
        // Same length, different bytes: only the SHA-256 check can catch it.
        await File.WriteAllTextAsync(Path.Combine(media.PackageRoot, "wifi", "wifi.inf"), "[Version]\r\nWIFI", Cancellation);
        using var fixture = new StepFixture(root, media.Set, sourceDiskNumber: 2);

        DeploymentStepResult result = await fixture.CreateStep().ExecuteAsync(fixture.Context, Cancellation);

        Assert.Equal(DeploymentStepState.Failed, result.State);
        Assert.Equal(DeploymentOperationNames.ExtractDriverPack, result.Failure?.OperationName);
        Assert.Equal("custom_driver_integrity", result.Failure?.Code);
        Assert.Null(fixture.Context.RuntimeState.ExtractedDriverPackPath);
        Assert.False(Directory.Exists(Path.Combine(fixture.CustomRoot, media.Set.ContentHash)));
    }

    [Fact]
    public async Task StagingFailsWhenMediaWasRemoved()
    {
        CustomDriverSetMedia media = await CustomDriverSetMedia.PublishAsync(Path.Combine(root, "media"), Cancellation);
        Directory.Delete(media.Set.MediaRoot, recursive: true);
        string targetWindows = Path.Combine(root, "Windows");
        var windows = new RecordingDriverApplicationService();
        var telemetry = new RecordingTelemetry();
        IDeploymentStep[] steps = DeploymentStepNames.ExecutionOrder.Select(name => name switch
        {
            DeploymentStepNames.PrepareTargetDiskLayout => new PrepareTargetStep(targetWindows),
            DeploymentStepNames.ExtractDriverPack => new ExtractDriverPackStep(new DriverPackStrategyResolver(),
                new UnusedExtractionService(), new CustomDriverSetStagingService()),
            DeploymentStepNames.ApplyDriverPack => new ApplyDriverPackStep(windows),
            DeploymentStepNames.ApplyRecoveryDrivers => new ApplyRecoveryDriversStep(windows),
            _ => (IDeploymentStep)new SucceedingStep(name)
        }).ToArray();
        var orchestrator = new DeploymentOrchestrator(new DriverApplicationOperationProgressService(), new DriverApplicationLogService(),
            new SourceDisk(2), steps, telemetry, NullLogger<DeploymentOrchestrator>.Instance);

        DeploymentResult result = await orchestrator.RunAsync(StepFixture.CreateRequest(root, media.Set) with { ApplyFirmwareUpdates = false }, Cancellation);

        Assert.False(result.IsSuccess);
        Assert.Equal(DeploymentStepNames.ExtractDriverPack, telemetry.Properties["deploy_session_failed_step_name"]);
        Assert.Equal("missing_driver_payload", telemetry.Properties["deploy_session_failure_code"]);
        Assert.Equal(0, windows.WindowsApplyCount);
        Assert.Equal(0, windows.RecoveryApplyCount);
        Assert.False(Directory.Exists(Path.Combine(targetWindows, "Foundry", "Extracted", "Drivers", "Custom", media.Set.ContentHash)));
    }

    [Fact]
    public async Task StagingRejectsSourceOnTargetDisk()
    {
        CustomDriverSetMedia media = await CustomDriverSetMedia.PublishAsync(Path.Combine(root, "media"), Cancellation);
        using var fixture = new StepFixture(root, media.Set, sourceDiskNumber: 1);

        DeploymentStepResult result = await fixture.CreateStep().ExecuteAsync(fixture.Context, Cancellation);

        Assert.Equal(DeploymentStepState.Failed, result.State);
        Assert.Equal(DeploymentOperationNames.ExtractDriverPack, result.Failure?.OperationName);
        Assert.Equal("custom_driver_integrity", result.Failure?.Code);
        Assert.False(Directory.Exists(fixture.CustomRoot));
        Assert.Null(fixture.Context.RuntimeState.ExtractedDriverPackPath);
    }

    [Fact]
    public async Task StagingSetsTheOfflineInfStateForTheApplySteps()
    {
        CustomDriverSetMedia media = await CustomDriverSetMedia.PublishAsync(Path.Combine(root, "media"), Cancellation);
        using var fixture = new StepFixture(root, media.Set, sourceDiskNumber: 2);

        DeploymentStepResult result = await fixture.CreateStep().ExecuteAsync(fixture.Context, Cancellation);

        Assert.Equal(DeploymentStepState.Succeeded, result.State);
        Assert.Equal(Path.Combine(fixture.CustomRoot, media.Set.ContentHash), fixture.Context.RuntimeState.ExtractedDriverPackPath);
        Assert.Equal(DriverPackInstallMode.OfflineInf, fixture.Context.RuntimeState.DriverPackInstallMode);
        Assert.Equal(nameof(DriverPackExtractionMethod.CustomDriverSetCopy), fixture.Context.RuntimeState.DriverPackExtractionMethod);
        Assert.Equal(media.Set.DisplayName, fixture.Context.RuntimeState.DriverPackName);
    }

    [Fact]
    public async Task DryRunSimulatesStaging()
    {
        CustomDriverSetMedia media = await CustomDriverSetMedia.PublishAsync(Path.Combine(root, "media"), Cancellation);
        using var fixture = new StepFixture(root, media.Set, sourceDiskNumber: 2, isDryRun: true);

        DeploymentStepResult result = await fixture.CreateStep().ExecuteAsync(fixture.Context, Cancellation);

        Assert.Equal(DeploymentStepState.Succeeded, result.State);
        Assert.Equal(DriverPackInstallMode.OfflineInf, fixture.Context.RuntimeState.DriverPackInstallMode);
        Assert.Single(Directory.EnumerateFiles(fixture.Context.RuntimeState.ExtractedDriverPackPath!, "*.inf"));
        Assert.False(Directory.Exists(Path.Combine(fixture.CustomRoot, media.Set.ContentHash)));
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }

    /// <summary>Runs the extract step for a custom set against a fake target whose source disk number the test chooses.</summary>
    private sealed class StepFixture : IDisposable
    {
        public StepFixture(string root, CustomDriverSetOption set, int sourceDiskNumber, bool isDryRun = false)
        {
            string windows = Path.Combine(root, "Windows");
            CustomRoot = Path.Combine(windows, "Foundry", "Extracted", "Drivers", "Custom");
            Context = new DeploymentStepExecutionContext(CreateRequest(root, set) with { IsDryRun = isDryRun },
                new DeploymentRuntimeState
                {
                    WorkspaceRoot = Path.Combine(root, "Workspace"),
                    TargetWindowsPartitionRoot = windows,
                    TargetFoundryRoot = Path.Combine(windows, "Foundry"),
                    DriverPackInstallMode = DriverPackInstallMode.OfflineInf
                }, [], new DriverApplicationOperationProgressService(), new DriverApplicationLogService(), new SourceDisk(sourceDiskNumber), _ => { });
        }

        public DeploymentStepExecutionContext Context { get; }

        public string CustomRoot { get; }

        public ExtractDriverPackStep CreateStep() =>
            new(new DriverPackStrategyResolver(), new UnusedExtractionService(), new CustomDriverSetStagingService());

        public static DeploymentContext CreateRequest(string root, CustomDriverSetOption set) => new()
        {
            Mode = DeploymentMode.Iso,
            CacheRootPath = Path.Combine(root, "Workspace"),
            TargetDiskNumber = 1,
            TargetComputerName = "LAB01",
            OperatingSystem = new OperatingSystemCatalogItem { Architecture = "x64", LicenseChannel = "VOL" },
            DriverPackSelectionKind = DriverPackSelectionKind.CustomDriverSet,
            CustomDriverSet = set
        };

        public void Dispose() => Context.Dispose();
    }

    private sealed class SourceDisk(int diskNumber) : ITargetDiskService
    {
        public Task<IReadOnlyList<TargetDiskInfo>> GetDisksAsync(CancellationToken cancellationToken = default, bool includeExcludedDisks = false) =>
            Task.FromResult<IReadOnlyList<TargetDiskInfo>>([]);

        public Task<int?> GetDiskNumberForPathAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult<int?>(diskNumber);
    }

    private sealed class UnusedExtractionService : IDriverPackExtractionService
    {
        public Task<DriverPackExtractionResult> ExtractAsync(DriverPackExecutionPlan executionPlan, string extractionRootPath,
            CancellationToken cancellationToken = default, IProgress<double>? progress = null) =>
            throw new InvalidOperationException("Custom driver sets are copied, never extracted.");
    }

    private sealed class PrepareTargetStep(string windows) : IDeploymentStep
    {
        public string Name => DeploymentStepNames.PrepareTargetDiskLayout;

        public Task<DeploymentStepResult> ExecuteAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
        {
            context.RuntimeState.TargetWindowsPartitionRoot = windows;
            context.RuntimeState.TargetFoundryRoot = Path.Combine(windows, "Foundry");
            Directory.CreateDirectory(context.RuntimeState.TargetFoundryRoot);
            return Task.FromResult(DeploymentStepResult.Succeeded("Prepared target layout."));
        }
    }

    private sealed class RecordingTelemetry : ITelemetryService
    {
        public IReadOnlyDictionary<string, object?> Properties { get; private set; } = new Dictionary<string, object?>();

        public Task TrackAsync(string eventName, IReadOnlyDictionary<string, object?> properties, CancellationToken cancellationToken = default)
        {
            Properties = new Dictionary<string, object?>(properties);
            return Task.CompletedTask;
        }

        public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class SucceedingStep(string name) : IDeploymentStep
    {
        public string Name => name;

        public Task<DeploymentStepResult> ExecuteAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken) =>
            Task.FromResult(DeploymentStepResult.Succeeded($"Completed {name}."));
    }
}

/// <summary>One custom driver set published to a media folder, with the Deploy settings bound to it and the discovered option.</summary>
internal sealed record CustomDriverSetMedia(DeployCustomDriversSettings Settings, CustomDriverSetOption Set, IVolumeDiscovery Volumes)
{
    /// <summary>Gets the media folder that holds the set's files.</summary>
    public string PackageRoot => Path.Combine(Set.MediaRoot, "Cache", "DriverPacks", "Custom", "Packages", Set.ContentHash, "files");

    /// <summary>Imports a three-file set (one in a subfolder), publishes it under <paramref name="root"/> and discovers it.</summary>
    public static async Task<CustomDriverSetMedia> PublishAsync(string root, CancellationToken cancellationToken)
    {
        string source = Path.Combine(root, "source");
        Directory.CreateDirectory(Path.Combine(source, "wifi"));
        await File.WriteAllTextAsync(Path.Combine(source, "net.inf"), "[Version]\r\nnet", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(source, "net.sys"), "driver net", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(source, "wifi", "wifi.inf"), "[Version]\r\nwifi", cancellationToken);
        var library = new CustomDriverSetLibrary(Path.Combine(root, "library"));
        ContentPackageReference reference = await library.ImportAsync(source, cancellationToken);
        CustomDriverSetReference set = new() { Id = "set0", DisplayName = "Private Lab Drivers", Package = reference };
        using WinPeContentPackageMediaLease lease = await WinPeCustomDriverSetMediaService.PrepareAsync(library,
            new() { IsEnabled = true, Sets = [set], DefaultSetId = set.Id }, cancellationToken);
        string media = Path.Combine(root, "volume");
        await new WinPeContentPackageMediaService(ContentPackageKind.Drivers).PublishAsync(lease, media, cancellationToken);
        var settings = new DeployCustomDriversSettings
        {
            IsEnabled = true,
            DefaultSetId = set.Id,
            ManifestId = lease.ManifestId,
            ManifestHash = lease.ManifestHash,
            Sets = [new() { Id = set.Id, DisplayName = set.DisplayName, ContentHash = reference.ContentHash, Length = reference.Length, FileCount = reference.FileCount }]
        };
        var volumes = new FixedVolumes(new VolumeInfo(media, "Foundry Cache", DriveType.Fixed, true, long.MaxValue));
        CustomDriverSetCatalog catalog = await new CustomDriverSetCatalogService(volumes).DiscoverAsync(settings, cancellationToken);
        return new(settings, Assert.Single(catalog.Sets), volumes);
    }

    /// <summary>A fixed volume list, so tests choose which media the catalog sees.</summary>
    internal sealed class FixedVolumes(params VolumeInfo[] volumes) : IVolumeDiscovery
    {
        public IReadOnlyList<VolumeInfo> GetVolumes() => volumes;
    }
}
