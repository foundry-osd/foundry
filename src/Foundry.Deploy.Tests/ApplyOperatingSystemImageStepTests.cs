// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Reflection;
using Foundry.Deploy.Models;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Cache;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.Deployment.Steps;
using Foundry.Deploy.Services.Download;
using Foundry.Deploy.Services.Hardware;
using Foundry.Deploy.Services.Logging;
using Foundry.Deploy.Services.Operations;

namespace Foundry.Deploy.Tests;

public sealed class ApplyOperatingSystemImageStepTests
{
    [Theory]
    [InlineData("missing-preflight")]
    [InlineData("missing-layout")]
    [InlineData("different-layout-disk")]
    [InlineData("different-layout-number")]
    [InlineData("changed-selection")]
    [InlineData("corrupt")]
    [InlineData("missing-file")]
    [InlineData("small-target")]
    [InlineData("small-volume")]
    [InlineData("unknown-volume")]
    [InlineData("readonly-volume")]
    [InlineData("missing-volume")]
    public async Task ExecuteAsync_RejectsUnsafeApply(string failure)
    {
        using var fixture = new Fixture(failure);
        DeploymentStepResult result = await fixture.Step.ExecuteAsync(fixture.Context, TestContext.Current.CancellationToken);
        Assert.Equal(DeploymentStepState.Failed, result.State);
        Assert.Equal(0, fixture.Native.ApplyCalls);
    }

    [Fact]
    public async Task ExecuteAsync_HoldsImageLockDuringHashInspectionAndApplyThenReleasesIt()
    {
        using var fixture = new Fixture("");
        DeploymentStepResult result = await fixture.Step.ExecuteAsync(fixture.Context, TestContext.Current.CancellationToken);
        Assert.Equal(DeploymentStepState.Succeeded, result.State);
        Assert.Equal(new[] { "hash", "inspect", "apply" }, fixture.Stages);
        File.WriteAllText(fixture.ImagePath, "released");
    }

    [Fact]
    public async Task ExecuteAsync_InspectionMismatchStopsApplyAndReleasesLease()
    {
        using var fixture = new Fixture("mismatch");
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Step.ExecuteAsync(fixture.Context, TestContext.Current.CancellationToken));
        Assert.Equal(0, fixture.Native.ApplyCalls);
        File.WriteAllText(fixture.ImagePath, "released");
    }

    [Fact]
    public async Task ExecuteAsync_PostApplyCallerCancellationPropagates()
    {
        using var fixture = new Fixture("cancel-postcheck");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Step.ExecuteAsync(fixture.Context, TestContext.Current.CancellationToken));
    }

    private sealed class Fixture : IDisposable, IArtifactDownloadService, IWindowsImageInspectionService, IVolumeStorageProbe
    {
        private readonly TempDeploymentWorkspace _workspace = TempDeploymentWorkspace.Create();
        private readonly string _failure;
        public List<string> Stages { get; } = [];
        public string ImagePath { get; }
        public NativeProxy Native { get; }
        public DeploymentStepExecutionContext Context { get; }
        public ApplyOperatingSystemImageStep Step { get; }
        public Fixture(string failure)
        {
            _failure = failure;
            ImagePath = Path.Combine(_workspace.RootPath, "image.esd");
            File.WriteAllText(ImagePath, "owned image");
            if (failure == "missing-file") File.Delete(ImagePath);
            var selection = new OperatingSystemCatalogItem
            {
                Edition = "Pro",
                Architecture = "x64",
                BuildMajor = 26100,
                BuildUbr = 1000,
                LanguageCode = "en-US",
                SizeBytes = 11,
                CatalogRevision = "catalog",
                SourceId = "source",
                FileName = "image.esd",
                Url = "https://dl.delivery.mp.microsoft.com/image.esd",
                Sha256 = new string('a', 64)
            };
            var request = new DeploymentContext
            {
                Mode = DeploymentMode.Iso,
                CacheRootPath = _workspace.CacheRuntimeRoot,
                TargetDiskNumber = 9,
                TargetComputerName = "TEST",
                OperatingSystem = selection,
                DriverPackSelectionKind = DriverPackSelectionKind.None,
                ConfirmedTargetDisk = new TargetDiskIdentity(9, "A", "SERIAL", failure == "small-target" ? 1UL : 256UL * 1024 * 1024 * 1024, "NVMe")
            };
            var state = new DeploymentRuntimeState
            {
                WorkspaceRoot = _workspace.WorkspaceRoot,
                Mode = DeploymentMode.Iso,
                TargetLayout = failure == "missing-layout" ? null : new DeploymentTargetLayout { DiskIdentity = failure == "different-layout-disk" ? request.ConfirmedTargetDisk! with { UniqueId = "another-device" } : request.ConfirmedTargetDisk, DiskNumber = failure == "different-layout-number" ? 8 : 9, WindowsPartitionRoot = _workspace.WindowsRoot, SystemPartitionRoot = _workspace.SystemRoot, RecoveryPartitionRoot = _workspace.RecoveryRoot, RecoveryPartitionLetter = 'R' },
                TargetWindowsPartitionRoot = _workspace.WindowsRoot,
                TargetSystemPartitionRoot = _workspace.SystemRoot,
                TargetFoundryRoot = Path.Combine(_workspace.WindowsRoot, "Foundry"),
                DownloadedOperatingSystemPath = ImagePath,
                ImagePreflight = failure == "missing-preflight" ? null : new DeploymentPreflightResult(ImagePreflightLevel.CompleteImageVerified, 0, ImagePath, null, null)
                { Selection = failure == "changed-selection" ? selection with { Edition = "Home" } : selection }
            };
            Context = new DeploymentStepExecutionContext(request, state, [], new FakeOperationProgressService(), new FakeDeploymentLogService(), new FakeTargetDiskService([]), _ => { });
            IWindowsImagingService service = DispatchProxy.Create<IWindowsImagingService, NativeProxy>();
            IBootRecoveryService boot = DispatchProxy.Create<IBootRecoveryService, NativeProxy>();
            Native = (NativeProxy)service;
            Native.OnApply = () => AssertLocked("apply");
            Native.CancelPostCheck = failure == "cancel-postcheck";
            Step = new ApplyOperatingSystemImageStep(service, boot, this, this, this);
        }
        private void AssertLocked(string stage)
        {
            Stages.Add(stage);
            Assert.Throws<IOException>(() => { using var writer = new FileStream(ImagePath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite); });
            Assert.Throws<IOException>(() => File.Delete(ImagePath));
        }
        public Task<ArtifactDownloadResult?> TryUseCachedAsync(ArtifactIdentity artifact, string path, CancellationToken cancellationToken = default)
        {
            Assert.Equal(ImagePath, path);
            AssertLocked("hash");
            return Task.FromResult<ArtifactDownloadResult?>(_failure == "corrupt" ? null : new ArtifactDownloadResult { DestinationPath = path, Downloaded = false, Method = "fake" });
        }
        public Task<ArtifactDownloadResult> DownloadAsync(ArtifactIdentity artifact, string path, CancellationToken cancellationToken = default, IProgress<DownloadProgress>? progress = null) => throw new NotSupportedException();
        public Task<WindowsImageInfo> InspectImageAsync(string imagePath, OperatingSystemCatalogItem selection, string workingDirectory, CancellationToken cancellationToken = default)
        {
            AssertLocked("inspect");
            if (_failure == "mismatch") throw new InvalidDataException("Image mismatch");
            return Task.FromResult(new WindowsImageInfo(4, "Professional", "x64", new Version(10, 0, 26100, 1000), "en-US", 20L * 1024 * 1024 * 1024));
        }
        public VolumeStorageStatus Inspect(string directory) => _failure switch
        {
            "small-volume" => new(true, true, 1),
            "unknown-volume" => new(true, true, null),
            "readonly-volume" => new(true, false, long.MaxValue),
            "missing-volume" => new(false, false, null),
            _ => new(true, true, 64L * 1024 * 1024 * 1024)
        };
        public void Dispose() { Context.Dispose(); _workspace.Dispose(); }
    }
    public class NativeProxy : DispatchProxy
    {
        public int ApplyCalls { get; private set; }
        public Action? OnApply { get; set; }
        public bool CancelPostCheck { get; set; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod!.Name)
            {
                case "ResolveImageIndexAsync": return Task.FromResult(4);
                case "ApplyImageAsync": ApplyCalls++; OnApply?.Invoke(); return Task.CompletedTask;
                case "ConfigureBootAsync": return Task.CompletedTask;
                case "GetAppliedWindowsEditionAsync": return CancelPostCheck ? Task.FromException<string?>(new OperationCanceledException()) : Task.FromResult<string?>("Professional");
                default: throw new NotSupportedException(targetMethod.Name);
            }
        }
    }
    private sealed class FakeDeploymentLogService : IDeploymentLogService
    {
        public DeploymentLogSession Initialize(string rootPath)
        {
            string logsDirectory = Path.Combine(rootPath, "Logs");
            string stateDirectory = Path.Combine(rootPath, "State");
            Directory.CreateDirectory(logsDirectory);
            Directory.CreateDirectory(stateDirectory);

            return new DeploymentLogSession
            {
                RootPath = rootPath,
                LogsDirectoryPath = logsDirectory,
                StateDirectoryPath = stateDirectory,
                StateFilePath = Path.Combine(stateDirectory, "deployment-state.json")
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

    private sealed class FakeTargetDiskService(IReadOnlyList<TargetDiskInfo> disks) : ITargetDiskService
    {
        public Task<IReadOnlyList<TargetDiskInfo>> GetDisksAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(disks);
        }

        public Task<int?> GetDiskNumberForPathAsync(string path, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<int?>(null);
        }
    }

    private sealed class TempDeploymentWorkspace : IDisposable
    {
        private TempDeploymentWorkspace(string rootPath)
        {
            RootPath = rootPath;
            WorkspaceRoot = Path.Combine(rootPath, "Workspace");
            CacheRuntimeRoot = Path.Combine(rootPath, "Foundry Cache", "Runtime");
            SystemRoot = Path.Combine(rootPath, "System");
            WindowsRoot = Path.Combine(rootPath, "Windows");
            RecoveryRoot = Path.Combine(rootPath, "Recovery");
            Directory.CreateDirectory(WorkspaceRoot);
            Directory.CreateDirectory(CacheRuntimeRoot);
        }

        public string RootPath { get; }
        public string WorkspaceRoot { get; }
        public string CacheRuntimeRoot { get; }
        public string SystemRoot { get; }
        public string WindowsRoot { get; }
        public string RecoveryRoot { get; }

        public static TempDeploymentWorkspace Create()
        {
            string rootPath = Path.Combine(Path.GetTempPath(), $"foundry-prepare-target-{Guid.NewGuid():N}");
            return new TempDeploymentWorkspace(rootPath);
        }

        public void Dispose()
        {
            Directory.Delete(RootPath, recursive: true);
        }
    }
}
