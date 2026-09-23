// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Models;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.Deployment.Steps;
using Foundry.Deploy.Services.Download;
using Foundry.Deploy.Services.DriverPacks;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class DriverPayloadStepTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExtractDriverPack_WhenDeferred_DoesNotClaimPreparationOrInvokeExtraction(bool dryRun)
    {
        using var fixture = new DriverApplicationStepTestFixture();
        using DeploymentStepExecutionContext original = CreateContext(fixture, DriverPackSelectionKind.OemCatalog,
            new DriverPackCatalogItem { Manufacturer = "Lenovo", FileName = "driver.exe" });
        using var context = new DeploymentStepExecutionContext(original.Request with { IsDryRun = dryRun },
            original.RuntimeState, [], new DriverApplicationOperationProgressService(), new DriverApplicationLogService(),
            new DriverApplicationTargetDiskService(), _ => { });
        context.RuntimeState.DriverPackInstallMode = DriverPackInstallMode.DeferredSetupComplete;
        context.RuntimeState.DownloadedDriverPackPath = fixture.CreateDriverPackage();
        var extraction = new RecordingExtractionService();

        DeploymentStepResult result = await new ExtractDriverPackStep(new DriverPackStrategyResolver(), extraction)
            .ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(DeploymentStepState.Skipped, result.State);
        Assert.Equal(0, extraction.CallCount);
        Assert.Null(context.RuntimeState.DeferredDriverPackagePath);
    }

    [Fact]
    public async Task FirmwareDryRun_SeparatesAcquisitionAndExtractionWithoutExternalOperations()
    {
        using var fixture = new DriverApplicationStepTestFixture();
        using DeploymentStepExecutionContext context = fixture.CreateContext(isDryRun: true);
        context.RuntimeState.HardwareProfile = new HardwareProfile { SystemFirmwareHardwareId = "test-firmware" };
        var extractor = new MicrosoftUpdateCatalogServiceTests.FakeArchiveExtractionService();
        var service = new MicrosoftUpdateCatalogFirmwareService(extractor,
            new MicrosoftUpdateCatalogServiceTests.FakeMicrosoftUpdateCatalogClient(), new PayloadDownloader(0),
            NullLogger<MicrosoftUpdateCatalogFirmwareService>.Instance);

        DeploymentStepResult download = await new DownloadFirmwareUpdateStep(service)
            .ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(DeploymentStepState.Succeeded, download.State);
        Assert.Null(context.RuntimeState.ExtractedFirmwarePath);
        DeploymentStepResult extraction = await new ExtractFirmwareUpdateStep(service)
            .ExecuteAsync(context, TestContext.Current.CancellationToken);
        DeploymentStepResult staging = await new ApplyFirmwareUpdateStep(fixture.DeploymentService)
            .ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(DeploymentStepState.Succeeded, extraction.State);
        Assert.Equal(DeploymentStepState.Succeeded, staging.State);
        Assert.Single(Directory.EnumerateFiles(context.RuntimeState.ExtractedFirmwarePath!, "*.inf"));
        Assert.Empty(extractor.SourcePaths);
        Assert.Equal(0, fixture.DeploymentService.WindowsApplyCount);
    }

    [Fact]
    public async Task CatalogUnavailable_SkipsRequestedLookupsAndClearsPayloads()
    {
        using var fixture = new DriverApplicationStepTestFixture();
        using DeploymentStepExecutionContext context = CreateContext(fixture, DriverPackSelectionKind.MicrosoftUpdateCatalog);
        context.RuntimeState.HardwareProfile = MicrosoftUpdateCatalogServiceTests.CreateHardwareProfile() with
        {
            SystemFirmwareHardwareId = "test-firmware"
        };
        context.RuntimeState.DownloadedDriverPackPath = fixture.CreateDriverPackage();
        context.RuntimeState.DownloadedFirmwarePath = fixture.WorkspaceRoot;
        var extractor = new MicrosoftUpdateCatalogServiceTests.FakeArchiveExtractionService();
        var catalog = new MicrosoftUpdateCatalogServiceTests.FakeMicrosoftUpdateCatalogClient { IsAvailable = false };
        var downloader = new PayloadDownloader(0);
        var drivers = new MicrosoftUpdateCatalogDriverService(extractor, catalog, downloader,
            NullLogger<MicrosoftUpdateCatalogDriverService>.Instance);
        var firmware = new MicrosoftUpdateCatalogFirmwareService(extractor, catalog, downloader,
            NullLogger<MicrosoftUpdateCatalogFirmwareService>.Instance);

        DeploymentStepResult driverResult = await new DownloadDriverPackStep(drivers, downloader)
            .ExecuteAsync(context, TestContext.Current.CancellationToken);
        DeploymentStepResult firmwareResult = await new DownloadFirmwareUpdateStep(firmware)
            .ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(DeploymentStepState.Skipped, driverResult.State);
        Assert.Equal(DeploymentStepState.Skipped, firmwareResult.State);
        Assert.Null(context.RuntimeState.DownloadedDriverPackPath);
        Assert.Empty(context.RuntimeState.MicrosoftUpdateCatalogDriverPaths);
        Assert.Null(context.RuntimeState.DownloadedFirmwarePath);
        Assert.Null(context.RuntimeState.ExtractedFirmwarePath);
    }

    [Theory]
    [InlineData(0, DeploymentStepState.Skipped)]
    [InlineData(1, DeploymentStepState.Succeeded)]
    public async Task DownloadFirmware_ReportsCacheDispositionWithoutExtracting(int downloads, DeploymentStepState expected)
    {
        using var fixture = new DriverApplicationStepTestFixture();
        using DeploymentStepExecutionContext context = CreateContext(fixture, DriverPackSelectionKind.None);
        context.RuntimeState.HardwareProfile = new HardwareProfile { SystemFirmwareHardwareId = "test-firmware" };
        var extractor = new MicrosoftUpdateCatalogServiceTests.FakeArchiveExtractionService();
        var firmwareService = new MicrosoftUpdateCatalogFirmwareService(extractor,
            new MicrosoftUpdateCatalogServiceTests.FakeMicrosoftUpdateCatalogClient(), new PayloadDownloader(downloads),
            NullLogger<MicrosoftUpdateCatalogFirmwareService>.Instance);

        DeploymentStepResult result = await new DownloadFirmwareUpdateStep(firmwareService)
            .ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.State);
        Assert.True(Directory.Exists(context.RuntimeState.DownloadedFirmwarePath));
        Assert.Null(context.RuntimeState.ExtractedFirmwarePath);
        Assert.Empty(extractor.SourcePaths);

        DeploymentStepResult extraction = await new ExtractFirmwareUpdateStep(firmwareService)
            .ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(DeploymentStepState.Succeeded, extraction.State);
        Assert.Single(extractor.SourcePaths);
        Assert.Single(Directory.EnumerateFiles(context.RuntimeState.ExtractedFirmwarePath!, "*.inf", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task ExtractFirmware_WhenSelectedPayloadDisappeared_Fails()
    {
        using var fixture = new DriverApplicationStepTestFixture();
        using DeploymentStepExecutionContext context = CreateContext(fixture, DriverPackSelectionKind.None);
        context.RuntimeState.DownloadedFirmwarePath = Path.Combine(fixture.WorkspaceRoot, "missing");
        var extractor = new MicrosoftUpdateCatalogServiceTests.FakeArchiveExtractionService();
        var firmwareService = new MicrosoftUpdateCatalogFirmwareService(extractor,
            new MicrosoftUpdateCatalogServiceTests.FakeMicrosoftUpdateCatalogClient(), new PayloadDownloader(0),
            NullLogger<MicrosoftUpdateCatalogFirmwareService>.Instance);

        DeploymentStepResult result = await new ExtractFirmwareUpdateStep(firmwareService)
            .ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(DeploymentStepState.Failed, result.State);
        Assert.Empty(extractor.SourcePaths);
    }

    [Theory]
    [InlineData(0, DeploymentStepState.Skipped)]
    [InlineData(1, DeploymentStepState.Succeeded)]
    [InlineData(2, DeploymentStepState.Succeeded)]
    public async Task DownloadCatalogDrivers_ReportsCacheDispositionAndPreservesPayloads(int downloads, DeploymentStepState expected)
    {
        using var fixture = new DriverApplicationStepTestFixture();
        using DeploymentStepExecutionContext context = CreateContext(fixture, DriverPackSelectionKind.MicrosoftUpdateCatalog);
        context.RuntimeState.HardwareProfile = MicrosoftUpdateCatalogServiceTests.CreateHardwareProfile(multipleDevices: true);
        var downloader = new PayloadDownloader(downloads);
        var catalogService = new MicrosoftUpdateCatalogDriverService(
            new MicrosoftUpdateCatalogServiceTests.FakeArchiveExtractionService(),
            new MicrosoftUpdateCatalogServiceTests.FakeMicrosoftUpdateCatalogClient(), downloader,
            NullLogger<MicrosoftUpdateCatalogDriverService>.Instance);
        var step = new DownloadDriverPackStep(catalogService, downloader);

        DeploymentStepResult result = await step.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.State);
        Assert.Equal(DriverPackInstallMode.OfflineInf, context.RuntimeState.DriverPackInstallMode);
        Assert.Equal(2, context.RuntimeState.MicrosoftUpdateCatalogDriverPaths.Count);
        Assert.All(context.RuntimeState.MicrosoftUpdateCatalogDriverPaths, path => Assert.True(File.Exists(path)));
    }

    [Fact]
    public async Task DownloadOemDriver_WhenCacheAccepted_SkipsAndPreservesDeferredStrategy()
    {
        using var fixture = new DriverApplicationStepTestFixture();
        using DeploymentStepExecutionContext context = CreateContext(fixture, DriverPackSelectionKind.OemCatalog,
            new DriverPackCatalogItem { Manufacturer = "Lenovo", FileName = "drivers.exe", DownloadUrl = "https://example.test/drivers.exe" });
        context.RuntimeState.DriverPackInstallMode = DriverPackInstallMode.DeferredSetupComplete;
        var downloader = new PayloadDownloader(0);
        var catalogService = new MicrosoftUpdateCatalogDriverService(
            new MicrosoftUpdateCatalogServiceTests.FakeArchiveExtractionService(),
            new MicrosoftUpdateCatalogServiceTests.FakeMicrosoftUpdateCatalogClient(), downloader,
            NullLogger<MicrosoftUpdateCatalogDriverService>.Instance);

        DeploymentStepResult result = await new DownloadDriverPackStep(catalogService, downloader)
            .ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(DeploymentStepState.Skipped, result.State);
        Assert.True(File.Exists(context.RuntimeState.DownloadedDriverPackPath));
        Assert.Equal(DriverPackInstallMode.DeferredSetupComplete, context.RuntimeState.DriverPackInstallMode);
    }

    [Fact]
    public async Task DownloadOemDriver_WhenSelectionMissing_Fails()
    {
        using var fixture = new DriverApplicationStepTestFixture();
        using DeploymentStepExecutionContext context = CreateContext(fixture, DriverPackSelectionKind.OemCatalog);
        var downloader = new PayloadDownloader(0);
        var catalogService = new MicrosoftUpdateCatalogDriverService(
            new MicrosoftUpdateCatalogServiceTests.FakeArchiveExtractionService(),
            new MicrosoftUpdateCatalogServiceTests.FakeMicrosoftUpdateCatalogClient(), downloader,
            NullLogger<MicrosoftUpdateCatalogDriverService>.Instance);

        DeploymentStepResult result = await new DownloadDriverPackStep(catalogService, downloader)
            .ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(DeploymentStepState.Failed, result.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApplySelectedFirmware_WhenPayloadEmpty_FailsWithoutServicing(bool dryRun)
    {
        using var fixture = new DriverApplicationStepTestFixture();
        using DeploymentStepExecutionContext context = fixture.CreateContext(dryRun);
        context.RuntimeState.DownloadedFirmwarePath = fixture.WorkspaceRoot;
        context.RuntimeState.ExtractedFirmwarePath = fixture.DriverRoot;
        File.Delete(Path.Combine(fixture.DriverRoot, "driver.inf"));

        DeploymentStepResult result = await new ApplyFirmwareUpdateStep(fixture.DeploymentService)
            .ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(DeploymentStepState.Failed, result.State);
        Assert.Equal(0, fixture.DeploymentService.WindowsApplyCount);
    }

    private static DeploymentStepExecutionContext CreateContext(DriverApplicationStepTestFixture fixture,
        DriverPackSelectionKind selection, DriverPackCatalogItem? driver = null)
    {
        using DeploymentStepExecutionContext original = fixture.CreateContext();
        return new DeploymentStepExecutionContext(original.Request with
        {
            DriverPackSelectionKind = selection,
            DriverPack = driver,
            OperatingSystem = new OperatingSystemCatalogItem { Architecture = "x64", ReleaseId = "24H2" }
        }, original.RuntimeState with { Mode = DeploymentMode.Iso }, [], new DriverApplicationOperationProgressService(),
            new DriverApplicationLogService(), new DriverApplicationTargetDiskService(), _ => { });
    }

    private sealed class PayloadDownloader(int downloads) : IArtifactDownloadService
    {
        private int _calls;

        public async Task<ArtifactDownloadResult> DownloadAsync(string sourceUrl, string destinationPath, string? expectedHash = null,
            long? expectedSizeBytes = null, string? artifactKind = null, CancellationToken cancellationToken = default,
            IProgress<DownloadProgress>? progress = null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await File.WriteAllTextAsync(destinationPath, "cab", cancellationToken);
            return new ArtifactDownloadResult { DestinationPath = destinationPath, Downloaded = _calls++ < downloads, Method = "test" };
        }
    }

    private sealed class RecordingExtractionService : IDriverPackExtractionService
    {
        public int CallCount { get; private set; }
        public Task<DriverPackExtractionResult> ExtractAsync(DriverPackExecutionPlan executionPlan, string extractionRootPath,
            CancellationToken cancellationToken = default, IProgress<double>? progress = null)
        {
            CallCount++;
            return Task.FromResult(new DriverPackExtractionResult { ExecutionPlan = executionPlan, InfCount = 0, Message = "No extraction." });
        }
    }
}
