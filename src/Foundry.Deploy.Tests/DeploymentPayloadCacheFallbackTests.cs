// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net;
using System.Security.Cryptography;
using Foundry.Deploy.Models;
using Foundry.Deploy.Services.Cache;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.Deployment.Steps;
using Foundry.Deploy.Services.Download;
using Foundry.Deploy.Services.DriverPacks;
using Foundry.Deploy.Services.Hardware;
using Foundry.Deploy.Services.Logging;
using Foundry.Deploy.Services.Operations;
using Foundry.Utilities.Processes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class DeploymentPayloadCacheFallbackTests
{
    [Fact]
    public async Task StorageWriteProbe_PreservesExistingPayloadAndRejectsReadOnlyFiles()
    {
        using TempDeploymentWorkspace workspace = TempDeploymentWorkspace.Create();
        string path = Path.Combine(workspace.UsbCacheRoot, "driver.cab");
        byte[] content = [1, 2, 3, 4];
        await File.WriteAllBytesAsync(path, content, TestContext.Current.CancellationToken);
        var storage = new DeploymentStorageService();

        Assert.True(storage.CanWriteDirectory(workspace.UsbCacheRoot, path));
        Assert.Equal(content, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            Assert.False(storage.CanWriteDirectory(workspace.UsbCacheRoot, path));
            Assert.Equal(content, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DriverDownload_WhenUsbIsReadOnly_UsesTargetPayload(bool catalogDrivers)
    {
        using TempDeploymentWorkspace workspace = TempDeploymentWorkspace.Create();
        using DeploymentStepExecutionContext context = CreateExecutionContext(workspace, catalogDrivers: catalogDrivers,
            storageService: new FixedStorageService(long.MaxValue, writable: false));
        var downloader = new CapturingArtifactDownloadService();
        var catalog = new MicrosoftUpdateCatalogDriverService(new MicrosoftUpdateCatalogServiceTests.FakeArchiveExtractionService(),
            new MicrosoftUpdateCatalogServiceTests.FakeMicrosoftUpdateCatalogClient(), downloader,
            NullLogger<MicrosoftUpdateCatalogDriverService>.Instance);

        DeploymentStepResult result = await new DownloadDriverPackStep(catalog, downloader)
            .ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(DeploymentStepState.Succeeded, result.State);
        Assert.StartsWith(workspace.TargetFoundryRoot, context.RuntimeState.DownloadedDriverPackPath);
        Assert.Empty(Directory.EnumerateFiles(workspace.UsbCacheRoot, "*.cab", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(0, true, 0, false, true)]
    [InlineData(long.MaxValue, false, 0, false, true)]
    [InlineData(long.MaxValue, false, 1024, false, true)]
    [InlineData(0, true, 1024, true, false)]
    [InlineData(0, true, 1024, false, false)]
    [InlineData(0, true, 512, false, true)]
    public async Task FirmwareDownload_UsesWritableCapacityAndValidatesSelectedCache(
        long availableBytes, bool writable, int existingBytes, bool validCache, bool targetCache)
    {
        using TempDeploymentWorkspace workspace = TempDeploymentWorkspace.Create();
        byte[] content = new byte[1024];
        new Random(91).NextBytes(content);
        string hash = Convert.ToHexString(SHA256.HashData(content));
        using DeploymentStepExecutionContext context = CreateExecutionContext(workspace,
            storageService: new FixedStorageService(availableBytes, writable));
        context.RuntimeState.HardwareProfile = new HardwareProfile { SystemFirmwareHardwareId = "firmware" };
        string relativePath = Path.Combine("Cache", "MicrosoftUpdateCatalog", "Firmware", "update-1", "driver-amd64.cab");
        string usbPath = Path.Combine(workspace.UsbCacheRoot, relativePath);
        if (existingBytes > 0)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(usbPath)!);
            byte[] cached = content[..existingBytes];
            if (!validCache) cached[0] ^= 0xff;
            await File.WriteAllBytesAsync(usbPath, cached, TestContext.Current.CancellationToken);
        }
        var handler = new PayloadHttpMessageHandler(content);
        using var client = new HttpClient(handler);
        var downloader = new ArtifactDownloadService(NullLogger<ArtifactDownloadService>.Instance, client);
        var firmware = new MicrosoftUpdateCatalogFirmwareService(new MicrosoftUpdateCatalogServiceTests.FakeArchiveExtractionService(),
            new MicrosoftUpdateCatalogServiceTests.FakeMicrosoftUpdateCatalogClient { Sha256 = hash, SizeInBytes = content.Length },
            downloader, NullLogger<MicrosoftUpdateCatalogFirmwareService>.Instance);

        DeploymentStepResult result = await new DownloadFirmwareUpdateStep(firmware)
            .ExecuteAsync(context, TestContext.Current.CancellationToken);

        string expectedCache = Path.Combine(targetCache ? workspace.TargetFoundryRoot : workspace.UsbCacheRoot, relativePath);
        Assert.True(File.Exists(expectedCache));
        Assert.Equal(content, await File.ReadAllBytesAsync(expectedCache, TestContext.Current.CancellationToken));
        Assert.Equal(validCache ? DeploymentStepState.Skipped : DeploymentStepState.Succeeded, result.State);
        Assert.Equal(validCache ? 0 : 1, handler.RequestCount);
        string stagedCab = Assert.Single(Directory.EnumerateFiles(context.RuntimeState.DownloadedFirmwarePath!, "*.cab", SearchOption.AllDirectories));
        Assert.Equal(content, await File.ReadAllBytesAsync(stagedCab, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FirmwareDownload_WithoutKnownSizeOrHash_UsesTargetStorage(bool missingHash)
    {
        using TempDeploymentWorkspace workspace = TempDeploymentWorkspace.Create();
        using DeploymentStepExecutionContext context = CreateExecutionContext(workspace,
            storageService: new FixedStorageService(long.MaxValue));
        context.RuntimeState.HardwareProfile = new HardwareProfile { SystemFirmwareHardwareId = "firmware" };
        var downloader = new CapturingArtifactDownloadService();
        var firmware = new MicrosoftUpdateCatalogFirmwareService(new MicrosoftUpdateCatalogServiceTests.FakeArchiveExtractionService(),
            new MicrosoftUpdateCatalogServiceTests.FakeMicrosoftUpdateCatalogClient
            {
                SizeInBytes = missingHash ? 1024 : 0,
                Sha256 = missingHash ? string.Empty : new string('B', 64),
                Sha1 = string.Empty
            }, downloader, NullLogger<MicrosoftUpdateCatalogFirmwareService>.Instance);

        DeploymentStepResult result = await new DownloadFirmwareUpdateStep(firmware)
            .ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(DeploymentStepState.Succeeded, result.State);
        Assert.StartsWith(workspace.TargetFoundryRoot, downloader.DestinationPath);
        Assert.Empty(Directory.EnumerateFiles(workspace.UsbCacheRoot, "*.cab", SearchOption.AllDirectories));
        Assert.Single(Directory.EnumerateFiles(context.RuntimeState.DownloadedFirmwarePath!, "*.cab", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(false, 1024, true)]
    [InlineData(false, 1024, false)]
    [InlineData(false, 512, false)]
    [InlineData(true, 1024, true)]
    [InlineData(true, 1024, false)]
    [InlineData(true, 512, false)]
    public async Task DriverDownload_OnFullUsb_AccountsOnlySelectedExistingBytesAndStillValidates(
        bool catalogDrivers, int existingBytes, bool validCache)
    {
        using TempDeploymentWorkspace workspace = TempDeploymentWorkspace.Create();
        byte[] content = new byte[1024];
        new Random(71).NextBytes(content);
        string expectedHash = Convert.ToHexString(SHA256.HashData(content));
        DeploymentStepExecutionContext context = CreateExecutionContext(workspace, driverPackSizeBytes: content.Length,
            expectedHash: expectedHash, catalogDrivers: catalogDrivers, storageService: new FixedStorageService(0));
        context.RuntimeState.HardwareProfile = MicrosoftUpdateCatalogServiceTests.CreateHardwareProfile();
        string relativePath = catalogDrivers
            ? Path.Combine("MicrosoftUpdateCatalog", "Drivers", "update-1", "driver-amd64.cab")
            : Path.Combine("DriverPacks", "Contoso", "drivers.cab");
        string usbPath = Path.Combine(workspace.UsbCacheRoot, "Cache", relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(usbPath)!);
        byte[] cached = content[..existingBytes];
        if (!validCache) cached[0] ^= 0xff;
        await File.WriteAllBytesAsync(usbPath, cached, TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(Path.GetDirectoryName(usbPath)!, "unrelated.cab"), content, TestContext.Current.CancellationToken);
        var handler = new PayloadHttpMessageHandler(content);
        using var client = new HttpClient(handler);
        var downloader = new ArtifactDownloadService(NullLogger<ArtifactDownloadService>.Instance, client);
        var catalog = new MicrosoftUpdateCatalogDriverService(
            new MicrosoftUpdateCatalogServiceTests.FakeArchiveExtractionService(),
            new MicrosoftUpdateCatalogServiceTests.FakeMicrosoftUpdateCatalogClient { Sha256 = expectedHash, SizeInBytes = content.Length },
            downloader, NullLogger<MicrosoftUpdateCatalogDriverService>.Instance);

        DeploymentStepResult result = await new DownloadDriverPackStep(catalog, downloader)
            .ExecuteAsync(context, TestContext.Current.CancellationToken);

        bool canReuseAllocation = existingBytes == content.Length;
        string expectedPath = canReuseAllocation ? usbPath : Path.Combine(workspace.TargetFoundryRoot, "Cache", relativePath);
        Assert.Equal(expectedPath, context.RuntimeState.DownloadedDriverPackPath);
        Assert.Equal(validCache ? DeploymentStepState.Skipped : DeploymentStepState.Succeeded, result.State);
        Assert.Equal(validCache ? 0 : 1, handler.RequestCount);
        Assert.Equal(content, await File.ReadAllBytesAsync(expectedPath, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(DeploymentMode.Usb, 1, false)]
    [InlineData(DeploymentMode.Usb, long.MaxValue, true)]
    [InlineData(DeploymentMode.Usb, 0, true)]
    [InlineData(DeploymentMode.Iso, 1, true)]
    public async Task CatalogDriverSteps_SelectCacheByCapacityAndExtractOnTarget(
        DeploymentMode mode, long sizeInBytes, bool targetCache)
    {
        using TempDeploymentWorkspace workspace = TempDeploymentWorkspace.Create();
        var downloadService = new CapturingArtifactDownloadService();
        var extractor = new MicrosoftUpdateCatalogServiceTests.FakeArchiveExtractionService();
        var catalogService = new MicrosoftUpdateCatalogDriverService(
            extractor, new MicrosoftUpdateCatalogServiceTests.FakeMicrosoftUpdateCatalogClient { SizeInBytes = sizeInBytes },
            downloadService, NullLogger<MicrosoftUpdateCatalogDriverService>.Instance);
        DeploymentStepExecutionContext context = CreateExecutionContext(workspace, mode: mode, catalogDrivers: true);
        var downloadStep = new DownloadDriverPackStep(catalogService, downloadService);

        DeploymentStepResult downloadResult = await downloadStep.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(DeploymentStepState.Succeeded, downloadResult.State);
        string expectedPath = Path.Combine(targetCache ? workspace.TargetFoundryRoot : workspace.UsbCacheRoot,
            "Cache", "MicrosoftUpdateCatalog", "Drivers", "update-1", "driver-amd64.cab");
        string secondPath = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(expectedPath))!, "update-2", "driver-amd64.cab");
        Assert.Equal(secondPath, downloadService.DestinationPath);
        Assert.Equal([expectedPath, secondPath], context.RuntimeState.MicrosoftUpdateCatalogDriverPaths);
        Assert.Empty(Directory.EnumerateFiles(workspace.WorkspaceRoot, "*.cab", SearchOption.AllDirectories));

        var processRunner = new Foundry.Deploy.Services.System.ProcessRunner(new ProcessRunner(),
            NullLogger<Foundry.Deploy.Services.System.ProcessRunner>.Instance);
        var extractionService = new DriverPackExtractionService(extractor, catalogService, processRunner,
            NullLogger<DriverPackExtractionService>.Instance);
        var extractStep = new ExtractDriverPackStep(new DriverPackStrategyResolver(), extractionService);
        DeploymentStepResult extractResult = await extractStep.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(DeploymentStepState.Succeeded, extractResult.State);
        Assert.Equal([expectedPath, secondPath], extractor.SourcePaths);
        Assert.StartsWith(Path.Combine(workspace.TargetFoundryRoot, "Extracted", "Drivers"), context.RuntimeState.ExtractedDriverPackPath);
        Assert.Equal(2, Directory.EnumerateFiles(context.RuntimeState.ExtractedDriverPackPath!, "*.inf", SearchOption.AllDirectories).Count());
    }

    [Fact]
    public async Task ExtractCatalogDrivers_WhenFirstSelectedCabDisappears_DoesNotSilentlySkipDrivers()
    {
        using TempDeploymentWorkspace workspace = TempDeploymentWorkspace.Create();
        DeploymentStepExecutionContext context = CreateExecutionContext(workspace, catalogDrivers: true);
        string missingPath = Path.Combine(workspace.UsbCacheRoot, "missing.cab");
        context.RuntimeState.DownloadedDriverPackPath = missingPath;
        context.RuntimeState.MicrosoftUpdateCatalogDriverPaths = [missingPath];
        var extractor = new MicrosoftUpdateCatalogServiceTests.FakeArchiveExtractionService();
        var catalogService = new MicrosoftUpdateCatalogDriverService(extractor,
            new MicrosoftUpdateCatalogServiceTests.FakeMicrosoftUpdateCatalogClient(), new CapturingArtifactDownloadService(),
            NullLogger<MicrosoftUpdateCatalogDriverService>.Instance);
        var processRunner = new Foundry.Deploy.Services.System.ProcessRunner(new ProcessRunner(),
            NullLogger<Foundry.Deploy.Services.System.ProcessRunner>.Instance);
        var step = new ExtractDriverPackStep(new DriverPackStrategyResolver(),
            new DriverPackExtractionService(extractor, catalogService, processRunner, NullLogger<DriverPackExtractionService>.Instance));

        await Assert.ThrowsAsync<FileNotFoundException>(() => step.ExecuteAsync(context, TestContext.Current.CancellationToken));
        Assert.Empty(extractor.SourcePaths);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DownloadOperatingSystemImageStep_UsesResolvedStorageRoute(bool usesTargetStorage)
    {
        using TempDeploymentWorkspace workspace = TempDeploymentWorkspace.Create();
        var downloadService = new CapturingArtifactDownloadService();
        using DeploymentStepExecutionContext context = CreateExecutionContext(
            workspace,
            operatingSystemSizeBytes: long.MaxValue);
        SetOperatingSystemPreflight(context, workspace, usesTargetStorage);
        var step = new DownloadOperatingSystemImageStep(downloadService);

        DeploymentStepResult result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(DeploymentStepState.Succeeded, result.State);
        Assert.Equal(
            Path.Combine(usesTargetStorage ? workspace.TargetFoundryRoot : workspace.UsbCacheRoot, "Cache", "OperatingSystems", "install.wim"),
            downloadService.DestinationPath);
    }

    [Fact]
    public async Task DownloadOperatingSystemImageStep_WithoutPreflight_DoesNotDownload()
    {
        using TempDeploymentWorkspace workspace = TempDeploymentWorkspace.Create();
        var downloadService = new CapturingArtifactDownloadService();
        DeploymentStepExecutionContext context = CreateExecutionContext(
            workspace,
            operatingSystemSizeBytes: 1);
        var step = new DownloadOperatingSystemImageStep(downloadService);

        DeploymentStepResult result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(DeploymentStepState.Failed, result.State);
        Assert.Equal("preflight_not_ready", result.Failure?.Code);
        Assert.Null(downloadService.DestinationPath);
    }

    [Fact]
    public async Task DownloadDriverPackStep_WhenUsbCacheHasInsufficientSpace_UsesTargetCache()
    {
        using TempDeploymentWorkspace workspace = TempDeploymentWorkspace.Create();
        var downloadService = new CapturingArtifactDownloadService();
        DeploymentStepExecutionContext context = CreateExecutionContext(
            workspace,
            driverPackSizeBytes: long.MaxValue);
        var step = new DownloadDriverPackStep(new FakeMicrosoftUpdateCatalogDriverService(), downloadService);

        DeploymentStepResult result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(DeploymentStepState.Succeeded, result.State);
        Assert.Equal(
            Path.Combine(workspace.TargetFoundryRoot, "Cache", "DriverPacks", "Contoso", "drivers.cab"),
            downloadService.DestinationPath);
    }

    [Fact]
    public async Task DownloadDriverPackStep_WhenUsbCacheHasEnoughSpace_UsesUsbCache()
    {
        using TempDeploymentWorkspace workspace = TempDeploymentWorkspace.Create();
        var downloadService = new CapturingArtifactDownloadService();
        DeploymentStepExecutionContext context = CreateExecutionContext(
            workspace,
            driverPackSizeBytes: 1);
        var step = new DownloadDriverPackStep(new FakeMicrosoftUpdateCatalogDriverService(), downloadService);

        DeploymentStepResult result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(DeploymentStepState.Succeeded, result.State);
        Assert.Equal(
            Path.Combine(workspace.UsbCacheRoot, "Cache", "DriverPacks", "Contoso", "drivers.cab"),
            downloadService.DestinationPath);
    }

    [Theory]
    [InlineData(DeploymentMode.Usb, false, false)]
    [InlineData(DeploymentMode.Usb, true, false)]
    [InlineData(DeploymentMode.Iso, false, false)]
    [InlineData(DeploymentMode.Iso, true, false)]
    [InlineData(DeploymentMode.Usb, false, true)]
    [InlineData(DeploymentMode.Usb, true, true)]
    [InlineData(DeploymentMode.Iso, false, true)]
    [InlineData(DeploymentMode.Iso, true, true)]
    public async Task DownloadStep_WhenCheckingCache_ReportsVerificationAndUsesExpectedBytes(
        DeploymentMode mode, bool driverPack, bool validCache)
    {
        using TempDeploymentWorkspace workspace = TempDeploymentWorkspace.Create();
        byte[] content = new byte[256 * 1024];
        new Random(53).NextBytes(content);
        string expectedHash = Convert.ToHexString(SHA256.HashData(content));
        var reports = new List<DeploymentStepProgress>();
        using DeploymentStepExecutionContext context = CreateExecutionContext(
            workspace, content.Length, content.Length, expectedHash, mode, reports.Add);
        if (!driverPack) SetOperatingSystemPreflight(context, workspace, mode == DeploymentMode.Iso);
        string cacheRoot = mode == DeploymentMode.Iso ? workspace.TargetFoundryRoot : workspace.UsbCacheRoot;
        string destinationPath = driverPack
            ? Path.Combine(cacheRoot, "Cache", "DriverPacks", "Contoso", "drivers.cab")
            : Path.Combine(cacheRoot, "Cache", "OperatingSystems", "install.wim");
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        byte[] cachedContent = (byte[])content.Clone();
        if (!validCache)
        {
            cachedContent[0] ^= 0xFF;
        }

        await File.WriteAllBytesAsync(destinationPath, cachedContent, TestContext.Current.CancellationToken);
        await ArtifactDownloadServiceTests.WriteLegacyManifestAsync(destinationPath, expectedHash, "SHA256");
        var handler = new PayloadHttpMessageHandler(content);
        using var client = new HttpClient(handler);
        var downloadService = new ArtifactDownloadService(NullLogger<ArtifactDownloadService>.Instance, client);
        DeploymentStepBase step = driverPack
            ? new DownloadDriverPackStep(new FakeMicrosoftUpdateCatalogDriverService(), downloadService)
            : new DownloadOperatingSystemImageStep(downloadService);
        context.SetCurrentStep(step, 1);

        DeploymentStepResult result = await step.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(validCache ? DeploymentStepState.Skipped : DeploymentStepState.Succeeded, result.State);
        string? returnedPath = driverPack
            ? context.RuntimeState.DownloadedDriverPackPath
            : context.RuntimeState.DownloadedOperatingSystemPath;
        Assert.Equal(destinationPath, returnedPath);
        Assert.Equal(content, await File.ReadAllBytesAsync(returnedPath!, TestContext.Current.CancellationToken));
        Assert.Equal(validCache ? 0 : 1, handler.RequestCount);
        DeploymentStepProgress[] verification = reports
            .Where(value => value.Message == "Checking cache..." && value.StepSubProgressPercent.HasValue)
            .ToArray();
        Assert.Equal(0d, verification[0].StepSubProgressPercent);
        Assert.Contains(verification, value => value.StepSubProgressPercent is > 0 and < 100);
        Assert.All(verification, value => Assert.StartsWith("Checking cache: ", value.StepSubProgressLabel));
        DeploymentStepProgress[] downloading = reports
            .Where(value => value.Message?.StartsWith("Downloading ", StringComparison.Ordinal) == true && value.StepSubProgressPercent.HasValue)
            .ToArray();
        if (validCache)
        {
            Assert.Equal(100d, verification[^1].StepSubProgressPercent);
            Assert.Empty(downloading);
        }
        else
        {
            Assert.DoesNotContain(verification, value => value.StepSubProgressPercent == 100);
            Assert.Equal(0d, downloading[0].StepSubProgressPercent);
            Assert.Equal(100d, downloading[^1].StepSubProgressPercent);
        }
    }

    private static void SetOperatingSystemPreflight(
        DeploymentStepExecutionContext context, TempDeploymentWorkspace workspace, bool usesTargetStorage)
    {
        context.Preflight = new DeploymentPreflightState
        {
            CacheRoot = context.RuntimeState.ResolvedCache!.RootPath,
            UsesTargetStorage = usesTargetStorage,
            ExternalImageDirectory = usesTargetStorage ? null : Path.Combine(workspace.UsbCacheRoot, "Cache", "OperatingSystems")
        };
    }

    private sealed class PayloadHttpMessageHandler(byte[] content) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) });
        }
    }

    private static DeploymentStepExecutionContext CreateExecutionContext(
        TempDeploymentWorkspace workspace,
        long operatingSystemSizeBytes = 1,
        long driverPackSizeBytes = 1,
        string expectedHash = "",
        DeploymentMode mode = DeploymentMode.Usb,
        Action<DeploymentStepProgress>? emitStepProgress = null,
        bool catalogDrivers = false,
        IDeploymentStorageService? storageService = null)
    {
        var request = new DeploymentContext
        {
            Mode = mode,
            CacheRootPath = workspace.UsbRuntimeRoot,
            TargetDiskNumber = 1,
            TargetComputerName = "LAB01",
            OperatingSystem = new OperatingSystemCatalogItem
            {
                FileName = "install.wim",
                Url = "https://example.test/install.wim",
                SizeBytes = operatingSystemSizeBytes,
                Sha256 = expectedHash
            },
            DriverPackSelectionKind = catalogDrivers ? DriverPackSelectionKind.MicrosoftUpdateCatalog : DriverPackSelectionKind.OemCatalog,
            DriverPack = new DriverPackCatalogItem
            {
                Manufacturer = "Contoso",
                Name = "Contoso Driver Pack",
                FileName = "drivers.cab",
                DownloadUrl = "https://example.test/drivers.cab",
                SizeBytes = driverPackSizeBytes,
                Sha256 = expectedHash
            },
            IsDryRun = false
        };

        var runtimeState = new DeploymentRuntimeState
        {
            WorkspaceRoot = workspace.WorkspaceRoot,
            Mode = mode,
            TargetFoundryRoot = workspace.TargetFoundryRoot,
            HardwareProfile = MicrosoftUpdateCatalogServiceTests.CreateHardwareProfile(multipleDevices: catalogDrivers),
            ResolvedCache = new CacheResolution
            {
                RootPath = workspace.UsbRuntimeRoot,
                Source = "test"
            }
        };

        return new DeploymentStepExecutionContext(
            request,
            runtimeState,
            [],
            new FakeOperationProgressService(),
            new FakeDeploymentLogService(),
            new FakeTargetDiskService(),
            emitStepProgress ?? (_ => { }), storageService);
    }

    private sealed class FixedStorageService(long availableBytes, bool writable = true) : IDeploymentStorageService
    {
        public long? GetAvailableBytes(string path) => availableBytes;
        public bool CanWriteDirectory(string path, string? existingFilePath = null) => writable;
    }

    private sealed class CapturingArtifactDownloadService : IArtifactDownloadService
    {
        public string? DestinationPath { get; private set; }

        public Task<ArtifactDownloadResult> DownloadAsync(
            string sourceUrl,
            string destinationPath,
            string? expectedHash = null,
            long? expectedSizeBytes = null,
            string? artifactKind = null,
            CancellationToken cancellationToken = default,
            IProgress<DownloadProgress>? progress = null)
        {
            DestinationPath = destinationPath;
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.WriteAllText(destinationPath, "cab");
            return Task.FromResult(new ArtifactDownloadResult
            {
                DestinationPath = destinationPath,
                Downloaded = true,
                Method = "test",
                SizeBytes = 1
            });
        }
    }

    private sealed class FakeMicrosoftUpdateCatalogDriverService : IMicrosoftUpdateCatalogDriverService
    {
        public Task<MicrosoftUpdateCatalogDriverResult> DownloadAsync(
            HardwareProfile hardwareProfile,
            OperatingSystemCatalogItem operatingSystem,
            string destinationDirectory,
            Func<long, string, string> resolveCacheDirectory,
            CancellationToken cancellationToken = default,
            IProgress<double>? progress = null)
        {
            return Task.FromResult(new MicrosoftUpdateCatalogDriverResult
            {
                DestinationDirectory = destinationDirectory,
                IsPayloadAvailable = false,
                Message = "No Microsoft Update Catalog payload."
            });
        }

        public Task<MicrosoftUpdateCatalogDriverResult> ExpandAsync(
            IReadOnlyList<string> sourcePaths,
            string destinationDirectory,
            CancellationToken cancellationToken = default,
            IProgress<double>? progress = null)
        {
            return Task.FromResult(new MicrosoftUpdateCatalogDriverResult
            {
                DestinationDirectory = destinationDirectory,
                IsPayloadAvailable = false,
                Message = "No Microsoft Update Catalog payload."
            });
        }
    }

    private sealed class TempDeploymentWorkspace : IDisposable
    {
        private TempDeploymentWorkspace(string rootPath)
        {
            WorkspaceRoot = Path.Combine(rootPath, "Workspace");
            UsbCacheRoot = Path.Combine(rootPath, "UsbCache");
            UsbRuntimeRoot = Path.Combine(UsbCacheRoot, "Runtime");
            TargetWindowsRoot = Path.Combine(rootPath, "TargetWindows");
            TargetFoundryRoot = Path.Combine(TargetWindowsRoot, "Foundry");

            Directory.CreateDirectory(WorkspaceRoot);
            Directory.CreateDirectory(UsbRuntimeRoot);
            Directory.CreateDirectory(TargetFoundryRoot);
        }

        public string WorkspaceRoot { get; }
        public string UsbCacheRoot { get; }
        public string UsbRuntimeRoot { get; }
        public string TargetWindowsRoot { get; }
        public string TargetFoundryRoot { get; }

        public static TempDeploymentWorkspace Create()
        {
            string rootPath = Path.Combine(Path.GetTempPath(), $"foundry-deploy-cache-{Guid.NewGuid():N}");
            return new TempDeploymentWorkspace(rootPath);
        }

        public void Dispose()
        {
            string rootPath = Directory.GetParent(WorkspaceRoot)?.FullName
                ?? throw new InvalidOperationException("Unable to resolve test workspace root.");
            Directory.Delete(rootPath, recursive: true);
        }
    }

    private sealed class FakeDeploymentLogService : IDeploymentLogService
    {
        public DeploymentLogSession Initialize(string rootPath)
        {
            return new DeploymentLogSession
            {
                RootPath = rootPath,
                LogsDirectoryPath = Path.Combine(rootPath, "Logs"),
                StateDirectoryPath = Path.Combine(rootPath, "State"),
                StateFilePath = Path.Combine(rootPath, "State", "deployment-state.json")
            };
        }

        public Task AppendAsync(
            DeploymentLogSession session,
            DeploymentLogLevel level,
            string message,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task SaveStateAsync<TState>(
            DeploymentLogSession session,
            TState state,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

    }

    private sealed class FakeOperationProgressService : IOperationProgressService
    {
        public bool IsOperationInProgress => false;
        public int Progress => 0;
        public string? Status => null;
        public OperationKind? CurrentOperation => null;
        public bool CanStartOperation => true;
        public event EventHandler? ProgressChanged;
        public bool TryStart(OperationKind kind, string initialStatus, int initialProgress = 0) => true;
        public void Report(int progress, string? status = null) => ProgressChanged?.Invoke(this, EventArgs.Empty);
        public void Complete(string? status = null) => ProgressChanged?.Invoke(this, EventArgs.Empty);
        public void Fail(string status) => ProgressChanged?.Invoke(this, EventArgs.Empty);
        public void ResetToIdle() => ProgressChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class FakeTargetDiskService : ITargetDiskService
    {
        public Task<IReadOnlyList<TargetDiskInfo>> GetDisksAsync(CancellationToken cancellationToken = default, bool includeExcludedDisks = false)
        {
            return Task.FromResult<IReadOnlyList<TargetDiskInfo>>([]);
        }

        public Task<int?> GetDiskNumberForPathAsync(string path, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<int?>(2);
        }
    }
}
