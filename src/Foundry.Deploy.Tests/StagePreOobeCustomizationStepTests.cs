// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Foundry.Deploy.Models;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Cache;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.Deployment.PreOobe;
using Foundry.Deploy.Services.Deployment.Steps;
using Foundry.Deploy.Services.DriverPacks;
using Foundry.Deploy.Services.Hardware;
using Foundry.Deploy.Services.Logging;
using Foundry.Deploy.Services.Operations;
using CoreDeployNetworkProfileRoamingSettings = Foundry.Core.Models.Configuration.Deploy.DeployNetworkProfileRoamingSettings;
using CoreDeployNetworkSettings = Foundry.Core.Models.Configuration.Deploy.DeployNetworkSettings;
using NetworkProfileRoamingTransportSettings = Foundry.Core.Models.Configuration.NetworkProfileRoamingTransportSettings;

namespace Foundry.Deploy.Tests;

public sealed class StagePreOobeCustomizationStepTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StageDriverInstaller_StagesOnlyPayloadWithoutSetupHook(bool dryRun)
    {
        using var tempDirectory = new TemporaryDirectory();
        DeploymentStepExecutionContext context = CreateContext(tempDirectory, isDryRun: dryRun);
        string source = Path.Combine(tempDirectory.RootPath, "driver.exe");
        await File.WriteAllBytesAsync(source, [1, 2, 3], TestContext.Current.CancellationToken);
        context.RuntimeState.DriverPackInstallMode = DriverPackInstallMode.DeferredSetupComplete;
        context.RuntimeState.DownloadedDriverPackPath = source;

        DeploymentStepResult result = await new StageDriverInstallerStep(new FakeDriverPackStrategyResolver())
            .ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(DeploymentStepState.Succeeded, result.State);
        Assert.Equal(Path.Combine(tempDirectory.WindowsRoot, "Windows", "Temp", "Foundry", "Payloads", "Drivers", "driver.exe"), context.RuntimeState.DeferredDriverPackagePath);
        Assert.Equal(!dryRun, File.Exists(context.RuntimeState.DeferredDriverPackagePath));
        if (!dryRun)
            Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(context.RuntimeState.DeferredDriverPackagePath!, TestContext.Current.CancellationToken));
        Assert.Null(context.RuntimeState.PreOobeSetupCompletePath);
        Assert.False(File.Exists(Path.Combine(tempDirectory.WindowsRoot, "Windows", "Setup", "Scripts", "SetupComplete.cmd")));
    }

    [Fact]
    public async Task ExecuteAsync_WhenDriverInstallerWasStaged_DoesNotRequireOrCopyOriginalDownload()
    {
        using var tempDirectory = new TemporaryDirectory();
        DeploymentStepExecutionContext context = CreateContext(tempDirectory);
        string stagedPath = Path.Combine(tempDirectory.WindowsRoot, "Windows", "Temp", "Foundry", "Payloads", "Drivers", "driver.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(stagedPath)!);
        await File.WriteAllBytesAsync(stagedPath, [1, 2, 3], TestContext.Current.CancellationToken);
        context.RuntimeState.DriverPackInstallMode = DriverPackInstallMode.DeferredSetupComplete;
        context.RuntimeState.DeferredDriverPackagePath = stagedPath;
        context.RuntimeState.DownloadedDriverPackPath = Path.Combine(tempDirectory.RootPath, "missing-download.exe");
        var step = new StagePreOobeCustomizationStep(
            new PreOobeScriptProvisioningService(new SetupCompleteScriptService()),
            new PreOobeScriptDefinitionBuilder(),
            new FakeDriverPackStrategyResolver());

        DeploymentStepResult result = await step.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(DeploymentStepState.Succeeded, result.State);
        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(stagedPath, TestContext.Current.CancellationToken));
        Assert.Contains(context.RuntimeState.PreOobeScriptPaths, path => path.EndsWith("Install-DriverPack.ps1", StringComparison.Ordinal));
        using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllTextAsync(context.RuntimeState.PreOobeManifestPath!, TestContext.Current.CancellationToken));
        Assert.Equal(context.RuntimeState.OperationId, manifest.RootElement.GetProperty("operationId").GetString());
        JsonElement driver = manifest.RootElement.GetProperty("scripts").EnumerateArray().Single(script => script.GetProperty("id").GetString() == "driver-pack");
        Assert.Contains(@"%SystemRoot%\Temp\Foundry\Payloads\Drivers\driver.exe", driver.GetProperty("arguments").EnumerateArray().Select(argument => argument.GetString()));
        Assert.Equal(@"Drivers\driver.exe", Assert.Single(driver.GetProperty("inputs").EnumerateArray()).GetProperty("relativePath").GetString());
    }

    [Theory]
    [InlineData("RET", false, false, true)]
    [InlineData("ret", false, true, true)]
    [InlineData("VOL", false, false, false)]
    [InlineData("RET", true, false, false)]
    [InlineData("RET", true, true, false)]
    [InlineData("", false, false, false)]
    [InlineData("OEM", false, false, false)]
    public async Task StagePreOobeCustomizationStep_OnlyStandardRetailDeploymentsStageOemActivation(
        string licenseChannel,
        bool usesCustomUnattend,
        bool isDryRun,
        bool expectsActivation)
    {
        using var tempDirectory = new TemporaryDirectory();
        using DeploymentStepExecutionContext context = CreateContext(tempDirectory, licenseChannel, usesCustomUnattend, isDryRun);
        var step = new StagePreOobeCustomizationStep(
            new PreOobeScriptProvisioningService(new SetupCompleteScriptService()),
            new PreOobeScriptDefinitionBuilder(),
            new FakeDriverPackStrategyResolver());

        DeploymentStepResult result = await step.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(expectsActivation ? DeploymentStepState.Succeeded : DeploymentStepState.Skipped, result.State);
        Assert.Equal(expectsActivation, context.RuntimeState.PreOobeScriptPaths.Any(
            path => path.EndsWith("Activate-WindowsOem.ps1", StringComparison.OrdinalIgnoreCase)));
        if (expectsActivation && !isDryRun)
        {
            Assert.True(File.Exists(context.RuntimeState.PreOobeSetupCompletePath));
            Assert.True(File.Exists(Assert.Single(context.RuntimeState.PreOobeScriptPaths)));
        }
    }

    [Fact]
    public async Task StagePreOobeCustomizationStep_WhenRoamingPayloadExists_StagesImporterAndCleanup()
    {
        using var tempDirectory = new TemporaryDirectory();
        DeploymentStepExecutionContext context = CreateContext(tempDirectory);
        var step = new StagePreOobeCustomizationStep(
            new PreOobeScriptProvisioningService(new SetupCompleteScriptService()),
            new PreOobeScriptDefinitionBuilder(),
            new FakeDriverPackStrategyResolver());
        context.NetworkProfileRoamingPayload = CreateRoamingPayload();

        DeploymentStepResult result = await step.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(DeploymentStepState.Succeeded, result.State);
        Assert.Contains(context.RuntimeState.PreOobeScriptPaths, path => path.EndsWith("Import-NetworkProfiles.ps1", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(context.RuntimeState.PreOobeScriptPaths, path => path.EndsWith("Cleanup-PreOobe.ps1", StringComparison.OrdinalIgnoreCase));
        Assert.True(File.Exists(Path.Combine(tempDirectory.WindowsRoot, "Windows", "Temp", "Foundry", "Payloads", "NetworkProfiles", "wifi-profile.xml")));
        Assert.Contains("network-profile-roaming", File.ReadAllText(context.RuntimeState.PreOobeManifestPath!));
        Assert.Contains("FOUNDRY PRE-OOBE BEGIN", File.ReadAllText(context.RuntimeState.PreOobeSetupCompletePath!));
    }

    [Fact]
    public async Task StagePreOobeCustomizationStep_WhenRetailWithDriversAndRoaming_StagesActivationBeforeCleanup()
    {
        using var tempDirectory = new TemporaryDirectory();
        string driverPackagePath = Path.Combine(tempDirectory.RootPath, "driver.exe");
        File.WriteAllBytes(driverPackagePath, [1, 2, 3]);
        DeploymentStepExecutionContext context = CreateContext(tempDirectory, licenseChannel: "RET");
        context.RuntimeState.DriverPackInstallMode = DriverPackInstallMode.DeferredSetupComplete;
        context.RuntimeState.DownloadedDriverPackPath = driverPackagePath;
        await new StageDriverInstallerStep(new FakeDriverPackStrategyResolver())
            .ExecuteAsync(context, TestContext.Current.CancellationToken);
        var step = new StagePreOobeCustomizationStep(
            new PreOobeScriptProvisioningService(new SetupCompleteScriptService()),
            new PreOobeScriptDefinitionBuilder(),
            new FakeDriverPackStrategyResolver());
        context.NetworkProfileRoamingPayload = CreateRoamingPayload();

        DeploymentStepResult result = await step.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(DeploymentStepState.Succeeded, result.State);
        Assert.Equal(Path.Combine(tempDirectory.WindowsRoot, "Windows", "Temp", "Foundry", "Payloads", "Drivers", "driver.exe"), context.RuntimeState.DeferredDriverPackagePath);
        Assert.True(File.Exists(context.RuntimeState.DeferredDriverPackagePath));
        Assert.Contains(context.RuntimeState.PreOobeScriptPaths, path => path.EndsWith("Install-DriverPack.ps1", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(context.RuntimeState.PreOobeScriptPaths, path => path.EndsWith("Import-NetworkProfiles.ps1", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(context.RuntimeState.PreOobeScriptPaths, path => path.EndsWith("Cleanup-PreOobe.ps1", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(context.RuntimeState.PreOobeScriptPaths, path => path.EndsWith("Activate-WindowsOem.ps1", StringComparison.OrdinalIgnoreCase));
        string runner = File.ReadAllText(context.RuntimeState.PreOobeRunnerPath!);
        Assert.True(
            runner.IndexOf("Install-DriverPack.ps1", StringComparison.Ordinal) <
            runner.IndexOf("Import-NetworkProfiles.ps1", StringComparison.Ordinal));
        Assert.True(
            runner.IndexOf("Import-NetworkProfiles.ps1", StringComparison.Ordinal) <
            runner.IndexOf("Activate-WindowsOem.ps1", StringComparison.Ordinal));
        Assert.True(
            runner.IndexOf("Activate-WindowsOem.ps1", StringComparison.Ordinal) <
            runner.IndexOf("Cleanup-PreOobe.ps1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StagePreOobeCustomizationStep_WhenDeferredDriverPayloadIsMissing_FailsWithoutStaging()
    {
        using var tempDirectory = new TemporaryDirectory();
        DeploymentStepExecutionContext context = CreateContext(tempDirectory);
        context.RuntimeState.DriverPackInstallMode = DriverPackInstallMode.DeferredSetupComplete;
        var step = new StagePreOobeCustomizationStep(
            new PreOobeScriptProvisioningService(new SetupCompleteScriptService()),
            new PreOobeScriptDefinitionBuilder(),
            new FakeDriverPackStrategyResolver());

        DeploymentStepResult result = await step.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(DeploymentStepState.Failed, result.State);
        Assert.Null(context.RuntimeState.DeferredDriverPackagePath);
        Assert.Null(context.RuntimeState.PreOobeSetupCompletePath);
        Assert.Empty(context.RuntimeState.PreOobeScriptPaths);
    }

    [Fact]
    public async Task StageDriverInstaller_WhenDeferredDriverCommandIsUnsupported_FailsWithoutStaging()
    {
        using var tempDirectory = new TemporaryDirectory();
        string driverPackagePath = Path.Combine(tempDirectory.RootPath, "driver.exe");
        File.WriteAllBytes(driverPackagePath, [1, 2, 3]);
        DeploymentStepExecutionContext context = CreateContext(tempDirectory);
        context.RuntimeState.DriverPackInstallMode = DriverPackInstallMode.DeferredSetupComplete;
        context.RuntimeState.DownloadedDriverPackPath = driverPackagePath;
        var step = new StageDriverInstallerStep(new FakeDriverPackStrategyResolver(DeferredDriverPackageCommandKind.None));

        DeploymentStepResult result = await step.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(DeploymentStepState.Failed, result.State);
        Assert.Null(context.RuntimeState.DeferredDriverPackagePath);
        Assert.Null(context.RuntimeState.PreOobeSetupCompletePath);
        Assert.Empty(context.RuntimeState.PreOobeScriptPaths);
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

    private static DeploymentStepExecutionContext CreateContext(
        TemporaryDirectory tempDirectory,
        string licenseChannel = "",
        bool usesCustomUnattend = false,
        bool isDryRun = false)
    {
        var request = new DeploymentContext
        {
            Mode = DeploymentMode.Iso,
            IsDryRun = isDryRun,
            Unattend = usesCustomUnattend
                ? new UnattendSelection(new Foundry.Core.Models.Configuration.Deploy.DeployUnattendFile(), "custom.xml")
                : null,
            CacheRootPath = tempDirectory.WorkspaceRoot,
            TargetDiskNumber = 1,
            TargetComputerName = "LAB01",
            OperatingSystem = new OperatingSystemCatalogItem { LicenseChannel = licenseChannel },
            DriverPackSelectionKind = DriverPackSelectionKind.OemCatalog,
            DriverPack = new DriverPackCatalogItem()
        };
        var runtimeState = new DeploymentRuntimeState
        {
            OperationId = "staging-operation",
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
                    WiredDot1x = new NetworkProfileRoamingTransportSettings { IsEnabled = true, IncludePrivateKeyMaterial = true },
                    Wifi = new NetworkProfileRoamingTransportSettings { IsEnabled = true, IncludePrivateKeyMaterial = true },
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

    private sealed class FakeDriverPackStrategyResolver(
        DeferredDriverPackageCommandKind commandKind = DeferredDriverPackageCommandKind.LenovoExecutable) : IDriverPackStrategyResolver
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
                DeferredCommandKind = commandKind,
                DownloadedPath = downloadedPath,
                EffectiveFileExtension = ".exe",
                Manufacturer = "Lenovo",
            };
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

        public Task AppendAsync(DeploymentLogSession session, DeploymentLogLevel level, string message, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task SaveStateAsync<TState>(DeploymentLogSession session, TState state, CancellationToken cancellationToken = default)
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
