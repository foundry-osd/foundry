// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Models;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Cache;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.Deployment.PreOobe;
using Foundry.Deploy.Services.Deployment.Steps;
using Foundry.Deploy.Services.DriverPacks;
using Foundry.Deploy.Services.Hardware;
using Foundry.Deploy.Services.Logging;
using Foundry.Deploy.Services.Network;
using Foundry.Deploy.Services.Operations;
using CoreDeployNetworkProfileRoamingSettings = Foundry.Core.Models.Configuration.Deploy.DeployNetworkProfileRoamingSettings;
using CoreDeployNetworkSettings = Foundry.Core.Models.Configuration.Deploy.DeployNetworkSettings;

namespace Foundry.Deploy.Tests;

public sealed class PreOobeNetworkProfileRoamingStepTests
{
    [Fact]
    public async Task StagePreOobeCustomizationStep_WhenRoamingPayloadExists_StagesImporterAndCleanup()
    {
        using var tempDirectory = new TemporaryDirectory();
        DeploymentStepExecutionContext context = CreateContext(tempDirectory);
        var step = new StagePreOobeCustomizationStep(
            new PreOobeScriptProvisioningService(new SetupCompleteScriptService()),
            new PreOobeScriptDefinitionBuilder(),
            new StaticNetworkProfileRoamingArtifactService(CreateRoamingPayload()));

        DeploymentStepResult result = await step.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(DeploymentStepState.Succeeded, result.State);
        Assert.Contains(context.RuntimeState.PreOobeScriptPaths, path => path.EndsWith("Import-NetworkProfiles.ps1", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(context.RuntimeState.PreOobeScriptPaths, path => path.EndsWith("Cleanup-PreOobe.ps1", StringComparison.OrdinalIgnoreCase));
        Assert.True(File.Exists(Path.Combine(tempDirectory.WindowsRoot, "Windows", "Temp", "Foundry", "PreOobe", "Data", "NetworkProfiles", "wifi-profile.xml")));
        Assert.Contains("network-profile-roaming", File.ReadAllText(context.RuntimeState.PreOobeManifestPath!));
        Assert.Contains("FOUNDRY PRE-OOBE BEGIN", File.ReadAllText(context.RuntimeState.PreOobeSetupCompletePath!));
    }

    [Fact]
    public async Task ApplyDriverPackStep_WhenDeferredDriverAndRoamingPayloadExist_StagesImporterWithDriverScripts()
    {
        using var tempDirectory = new TemporaryDirectory();
        string driverPackagePath = Path.Combine(tempDirectory.RootPath, "driver.exe");
        File.WriteAllBytes(driverPackagePath, [1, 2, 3]);
        DeploymentStepExecutionContext context = CreateContext(tempDirectory);
        context.RuntimeState.DriverPackInstallMode = DriverPackInstallMode.DeferredSetupComplete;
        context.RuntimeState.DownloadedDriverPackPath = driverPackagePath;
        var step = new ApplyDriverPackStep(
            new FakeWindowsDeploymentService(),
            new PreOobeScriptProvisioningService(new SetupCompleteScriptService()),
            new PreOobeScriptDefinitionBuilder(),
            new FakeDriverPackStrategyResolver(),
            new StaticNetworkProfileRoamingArtifactService(CreateRoamingPayload()));

        DeploymentStepResult result = await step.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(DeploymentStepState.Succeeded, result.State);
        Assert.Contains(context.RuntimeState.PreOobeScriptPaths, path => path.EndsWith("Install-DriverPack.ps1", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(context.RuntimeState.PreOobeScriptPaths, path => path.EndsWith("Import-NetworkProfiles.ps1", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(context.RuntimeState.PreOobeScriptPaths, path => path.EndsWith("Cleanup-PreOobe.ps1", StringComparison.OrdinalIgnoreCase));
        string runner = File.ReadAllText(context.RuntimeState.PreOobeRunnerPath!);
        Assert.True(
            runner.IndexOf("Install-DriverPack.ps1", StringComparison.Ordinal) <
            runner.IndexOf("Import-NetworkProfiles.ps1", StringComparison.Ordinal));
        Assert.True(
            runner.IndexOf("Import-NetworkProfiles.ps1", StringComparison.Ordinal) <
            runner.IndexOf("Cleanup-PreOobe.ps1", StringComparison.Ordinal));
    }

    private static PreOobeNetworkProfileRoamingPayload CreateRoamingPayload()
    {
        return new PreOobeNetworkProfileRoamingPayload
        {
            DataFiles =
            [
                new PreOobeScriptDataFile
                {
                    FileName = Path.Combine("NetworkProfiles", "wifi-profile.xml"),
                    Content = "<WLANProfile />"
                },
                new PreOobeScriptDataFile
                {
                    FileName = Path.Combine("NetworkProfiles", "import-settings.json"),
                    Content = "{}"
                }
            ]
        };
    }

    private static DeploymentStepExecutionContext CreateContext(TemporaryDirectory tempDirectory)
    {
        var request = new DeploymentContext
        {
            Mode = DeploymentMode.Iso,
            IsDryRun = false,
            CacheRootPath = tempDirectory.WorkspaceRoot,
            TargetDiskNumber = 1,
            TargetComputerName = "LAB01",
            OperatingSystem = new OperatingSystemCatalogItem(),
            DriverPackSelectionKind = DriverPackSelectionKind.OemCatalog,
            DriverPack = new DriverPackCatalogItem()
        };
        var runtimeState = new DeploymentRuntimeState
        {
            WorkspaceRoot = tempDirectory.WorkspaceRoot,
            Mode = DeploymentMode.Iso,
            TargetWindowsPartitionRoot = tempDirectory.WindowsRoot,
            TargetFoundryRoot = tempDirectory.TargetFoundryRoot,
            ResolvedCache = new CacheResolution
            {
                RootPath = tempDirectory.WorkspaceRoot,
                Source = "test"
            },
            Network = new CoreDeployNetworkSettings
            {
                ProfileRoaming = new CoreDeployNetworkProfileRoamingSettings
                {
                    IsEnabled = true,
                    IncludePrivateKeyMaterial = true,
                    ArtifactRootPath = tempDirectory.RootPath
                }
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

    private sealed class StaticNetworkProfileRoamingArtifactService(PreOobeNetworkProfileRoamingPayload payload) : INetworkProfileRoamingArtifactService
    {
        public Task<PreOobeNetworkProfileRoamingPayload?> LoadAsync(
            CoreDeployNetworkProfileRoamingSettings settings,
            string workspaceRootPath,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<PreOobeNetworkProfileRoamingPayload?>(payload);
        }
    }

    private sealed class FakeDriverPackStrategyResolver : IDriverPackStrategyResolver
    {
        public DriverPackExecutionPlan Resolve(
            DriverPackSelectionKind selectionKind,
            DriverPackCatalogItem? driverPack,
            string downloadedPath)
        {
            return new DriverPackExecutionPlan
            {
                InstallMode = DriverPackInstallMode.DeferredSetupComplete,
                ExtractionMethod = DriverPackExtractionMethod.None,
                DeferredCommandKind = DeferredDriverPackageCommandKind.LenovoExecutable,
                DownloadedPath = downloadedPath,
                EffectiveFileExtension = ".exe",
                Manufacturer = "Lenovo",
                RequiresInfPayload = false
            };
        }
    }

    private sealed class FakeWindowsDeploymentService : IWindowsDeploymentService
    {
        public Task<DeploymentTargetLayout> PrepareTargetDiskAsync(int diskNumber, string workingDirectory, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<int> ResolveImageIndexAsync(string imagePath, string requestedEdition, string workingDirectory, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task ApplyImageAsync(string imagePath, int imageIndex, string windowsPartitionRoot, string scratchDirectory, string workingDirectory, CancellationToken cancellationToken = default, IProgress<double>? progress = null)
        {
            throw new NotSupportedException();
        }

        public Task<string?> GetAppliedWindowsEditionAsync(string windowsPartitionRoot, string workingDirectory, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task ConfigureOfflineComputerNameAsync(string windowsPartitionRoot, string computerName, string processorArchitecture, string? defaultTimeZoneId = null, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task ConfigureOfflineOobeAsync(string windowsPartitionRoot, DeployOobeSettings settings, string processorArchitecture, string workingDirectory, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task ConfigureOfflineAiComponentRemovalAsync(string windowsPartitionRoot, DeployAiComponentRemovalSettings settings, string workingDirectory, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task ConfigureRecoveryEnvironmentAsync(string windowsPartitionRoot, string recoveryPartitionRoot, string workingDirectory, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task SealRecoveryPartitionAsync(string recoveryPartitionRoot, char recoveryPartitionLetter, string workingDirectory, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task ApplyOfflineDriversAsync(string windowsPartitionRoot, string driverRoot, string scratchDirectory, string workingDirectory, CancellationToken cancellationToken = default, IProgress<double>? progress = null)
        {
            throw new NotSupportedException();
        }

        public Task ApplyRecoveryDriversAsync(string recoveryPartitionRoot, string driverRoot, string scratchDirectory, string workingDirectory, CancellationToken cancellationToken = default, IProgress<double>? mountProgress = null, IProgress<double>? applyProgress = null, IProgress<double>? unmountProgress = null, Action? onMountStarted = null, Action? onApplyStarted = null, Action? onUnmountStarted = null)
        {
            throw new NotSupportedException();
        }

        public Task ConfigureBootAsync(string windowsPartitionRoot, string systemPartitionRoot, int operatingSystemBuildMajor, string workingDirectory, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
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
                LogFilePath = Path.Combine(logsDirectory, "FoundryDeploy.log"),
                StateFilePath = Path.Combine(stateDirectory, "deployment-state.json")
            };
        }

        public Task AppendAsync(DeploymentLogSession session, DeploymentLogLevel level, string message, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task SaveStateAsync<TState>(DeploymentLogSession session, TState state, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public void Release(DeploymentLogSession session)
        {
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

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            RootPath = Path.Combine(Path.GetTempPath(), "Foundry.Deploy.Tests", Guid.NewGuid().ToString("N"));
            WorkspaceRoot = Path.Combine(RootPath, "Workspace");
            WindowsRoot = Path.Combine(RootPath, "Windows");
            TargetFoundryRoot = Path.Combine(WindowsRoot, "Foundry");
            Directory.CreateDirectory(WorkspaceRoot);
            Directory.CreateDirectory(WindowsRoot);
            Directory.CreateDirectory(TargetFoundryRoot);
        }

        public string RootPath { get; }

        public string WorkspaceRoot { get; }

        public string WindowsRoot { get; }

        public string TargetFoundryRoot { get; }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
