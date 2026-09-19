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
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class DeploymentPayloadCacheFallbackTests
{
    [Fact]
    public async Task DownloadOperatingSystemImageStep_WhenUsbCacheHasInsufficientSpace_UsesTargetCache()
    {
        using TempDeploymentWorkspace workspace = TempDeploymentWorkspace.Create();
        var downloadService = new CapturingArtifactDownloadService();
        DeploymentStepExecutionContext context = CreateExecutionContext(
            workspace,
            operatingSystemSizeBytes: long.MaxValue);
        var step = new DownloadOperatingSystemImageStep(downloadService);

        DeploymentStepResult result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(DeploymentStepState.Succeeded, result.State);
        Assert.Equal(
            Path.Combine(workspace.TargetFoundryRoot, "Cache", "OperatingSystems", "install.wim"),
            downloadService.DestinationPath);
    }

    [Fact]
    public async Task DownloadOperatingSystemImageStep_WhenUsbCacheHasEnoughSpace_UsesUsbCache()
    {
        using TempDeploymentWorkspace workspace = TempDeploymentWorkspace.Create();
        var downloadService = new CapturingArtifactDownloadService();
        DeploymentStepExecutionContext context = CreateExecutionContext(
            workspace,
            operatingSystemSizeBytes: 1);
        var step = new DownloadOperatingSystemImageStep(downloadService);

        DeploymentStepResult result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(DeploymentStepState.Succeeded, result.State);
        Assert.Equal(
            Path.Combine(workspace.UsbCacheRoot, "Cache", "OperatingSystems", "install.wim"),
            downloadService.DestinationPath);
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
        DeploymentStepExecutionContext context = CreateExecutionContext(
            workspace, content.Length, content.Length, expectedHash, mode, reports.Add);
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

        Assert.Equal(DeploymentStepState.Succeeded, result.State);
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
        Action<DeploymentStepProgress>? emitStepProgress = null)
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
            DriverPackSelectionKind = DriverPackSelectionKind.OemCatalog,
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
            emitStepProgress ?? (_ => { }));
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
            IProgress<DownloadProgress>? progress = null,
            bool allowDownload = true)
        {
            DestinationPath = destinationPath;
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
            string cacheDirectory,
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
            string sourceDirectory,
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
            return Task.FromResult<int?>(null);
        }
    }
}
