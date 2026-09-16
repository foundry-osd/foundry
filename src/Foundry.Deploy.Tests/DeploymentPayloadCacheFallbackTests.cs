// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net;
using System.Security.Cryptography;
using System.Text;
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
    [InlineData(DeploymentMode.Usb, false)]
    [InlineData(DeploymentMode.Usb, true)]
    [InlineData(DeploymentMode.Iso, false)]
    [InlineData(DeploymentMode.Iso, true)]
    public async Task DownloadStep_WhenCacheManifestIsForged_HandsVerifiedBytesToNextStep(DeploymentMode mode, bool driverPack)
    {
        using TempDeploymentWorkspace workspace = TempDeploymentWorkspace.Create();
        byte[] content = Encoding.UTF8.GetBytes("original-content");
        string expectedHash = Convert.ToHexString(SHA256.HashData(content));
        DeploymentStepExecutionContext context = CreateExecutionContext(
            workspace, content.Length, content.Length, expectedHash, mode);
        string cacheRoot = mode == DeploymentMode.Iso ? workspace.TargetFoundryRoot : workspace.UsbCacheRoot;
        string destinationPath = driverPack
            ? Path.Combine(cacheRoot, "Cache", "DriverPacks", "Contoso", "drivers.cab")
            : Path.Combine(cacheRoot, "Cache", "OperatingSystems", "install.wim");
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        await File.WriteAllTextAsync(destinationPath, "tampered-content", TestContext.Current.CancellationToken);
        await ArtifactDownloadServiceTests.WriteLegacyManifestAsync(destinationPath, expectedHash, "SHA256");
        using var client = new HttpClient(new PayloadHttpMessageHandler(content));
        var downloadService = new ArtifactDownloadService(NullLogger<ArtifactDownloadService>.Instance, client);
        DeploymentStepBase step = driverPack
            ? new DownloadDriverPackStep(new FakeMicrosoftUpdateCatalogDriverService(), downloadService)
            : new DownloadOperatingSystemImageStep(downloadService);

        DeploymentStepResult result = await step.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(DeploymentStepState.Succeeded, result.State);
        string? returnedPath = driverPack
            ? context.RuntimeState.DownloadedDriverPackPath
            : context.RuntimeState.DownloadedOperatingSystemPath;
        Assert.Equal(destinationPath, returnedPath);
        Assert.Equal(content, await File.ReadAllBytesAsync(returnedPath!, TestContext.Current.CancellationToken));
    }

    private sealed class PayloadHttpMessageHandler(byte[] content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) });
    }

    private static DeploymentStepExecutionContext CreateExecutionContext(
        TempDeploymentWorkspace workspace,
        long operatingSystemSizeBytes = 1,
        long driverPackSizeBytes = 1,
        string expectedHash = "",
        DeploymentMode mode = DeploymentMode.Usb)
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
            _ => { });
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
        public Task<IReadOnlyList<TargetDiskInfo>> GetDisksAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<TargetDiskInfo>>([]);
        }

        public Task<int?> GetDiskNumberForPathAsync(string path, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<int?>(null);
        }
    }
}
