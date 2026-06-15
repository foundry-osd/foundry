using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.WinPe;
using Foundry.Core.Services.WinPe.OsRecovery;
using Foundry.Deploy.Models;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Configuration;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.Deployment.Steps;
using Foundry.Deploy.Services.Hardware;
using Foundry.Deploy.Services.Logging;
using Foundry.Deploy.Services.Operations;
using Foundry.Deploy.Services.System;
using CoreDeployNetworkSettings = Foundry.Core.Models.Configuration.Deploy.DeployNetworkSettings;
using CoreDeployNetworkProfileRoamingSettings = Foundry.Core.Models.Configuration.Deploy.DeployNetworkProfileRoamingSettings;

namespace Foundry.Deploy.Tests;

public sealed class ProvisionOsRecoveryStepTests
{
    [Fact]
    public async Task ExecuteAsync_WhenOsRecoveryIsDisabled_SkipsWithoutProvisioning()
    {
        using TempWorkspace workspace = TempWorkspace.Create();
        var provisioning = new RecordingOsRecoveryPayloadProvisioningService();
        ProvisionOsRecoveryStep step = CreateStep(provisioning);
        DeploymentStepExecutionContext context = CreateContext(workspace.RootPath, isOsRecoveryEnabled: false);

        DeploymentStepResult result = await step.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(DeploymentStepState.Skipped, result.State);
        Assert.Equal("OS Recovery is disabled.", result.Message);
        Assert.Equal(0, provisioning.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_WhenRunningFromRecoveryMode_SkipsWithoutProvisioning()
    {
        using TempWorkspace workspace = TempWorkspace.Create();
        var provisioning = new RecordingOsRecoveryPayloadProvisioningService();
        ProvisionOsRecoveryStep step = CreateStep(provisioning);
        DeploymentStepExecutionContext context = CreateContext(
            workspace.RootPath,
            isOsRecoveryEnabled: true,
            mode: DeploymentMode.Recovery);

        DeploymentStepResult result = await step.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(DeploymentStepState.Skipped, result.State);
        Assert.Equal("OS Recovery provisioning is skipped in recovery mode.", result.Message);
        Assert.Equal(0, provisioning.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_WhenRecoveryPartitionIsUnavailable_FailsBeforeProvisioning()
    {
        using TempWorkspace workspace = TempWorkspace.Create();
        var provisioning = new RecordingOsRecoveryPayloadProvisioningService();
        ProvisionOsRecoveryStep step = CreateStep(provisioning);
        DeploymentStepExecutionContext context = CreateContext(
            workspace.RootPath,
            isOsRecoveryEnabled: true,
            mode: DeploymentMode.Iso);

        DeploymentStepResult result = await step.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(DeploymentStepState.Failed, result.State);
        Assert.Equal("Recovery partition is unavailable.", result.Message);
        Assert.Equal(0, provisioning.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_WhenDryRunAndEnabled_SucceedsWithoutProvisioning()
    {
        using TempWorkspace workspace = TempWorkspace.Create();
        var provisioning = new RecordingOsRecoveryPayloadProvisioningService();
        ProvisionOsRecoveryStep step = CreateStep(provisioning);
        DeploymentStepExecutionContext context = CreateContext(
            workspace.RootPath,
            isOsRecoveryEnabled: true,
            isDryRun: true);

        DeploymentStepResult result = await step.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(DeploymentStepState.Succeeded, result.State);
        Assert.Equal("OS Recovery provisioned (simulation).", result.Message);
        Assert.Equal(0, provisioning.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_WhenLiveAndEnabled_ProvisionsSanitizedRecoveryPayload()
    {
        using TempWorkspace workspace = TempWorkspace.Create();
        string windowsRoot = Path.Combine(workspace.RootPath, "W");
        string recoveryRoot = Path.Combine(workspace.RootPath, "R");
        Directory.CreateDirectory(Path.Combine(windowsRoot, "Windows"));
        Directory.CreateDirectory(Path.Combine(recoveryRoot, "Recovery", "WindowsRE"));
        await File.WriteAllTextAsync(
            Path.Combine(recoveryRoot, "Recovery", "WindowsRE", "winre.wim"),
            "wim",
            TestContext.Current.CancellationToken);

        string deployConfigurationPath = Path.Combine(workspace.RootPath, "foundry.deploy.config.json");
        FoundryDeployConfigurationDocument sourceDocument = new()
        {
            OsRecovery = new DeployOsRecoverySettings
            {
                IsEnabled = true
            },
            Autopilot = new DeployAutopilotSettings
            {
                IsEnabled = true,
                DefaultProfileFolderName = "profile"
            },
            Network = new CoreDeployNetworkSettings
            {
                ProfileRoaming = new CoreDeployNetworkProfileRoamingSettings
                {
                    IsEnabled = true,
                    IncludePrivateKeyMaterial = true,
                    ArtifactRootPath = @"X:\Foundry\Config\Network"
                }
            }
        };
        await File.WriteAllTextAsync(
            deployConfigurationPath,
            System.Text.Json.JsonSerializer.Serialize(sourceDocument, ConfigurationJsonDefaults.SerializerOptions),
            TestContext.Current.CancellationToken);

        var provisioning = new RecordingOsRecoveryPayloadProvisioningService();
        var processRunner = new RecordingProcessRunner();
        ProvisionOsRecoveryStep step = new(
            provisioning,
            new FakeEmbeddedAssetService(),
            new FakeDeployConfigurationService(deployConfigurationPath),
            processRunner,
            winReConfigToolPath: Path.Combine(workspace.RootPath, "winrecfg.exe"));
        DeploymentStepExecutionContext context = CreateContext(
            workspace.RootPath,
            isOsRecoveryEnabled: true,
            mode: DeploymentMode.Iso,
            isDryRun: false,
            runtimeState =>
            {
                runtimeState.TargetWindowsPartitionRoot = windowsRoot;
                runtimeState.TargetRecoveryPartitionRoot = recoveryRoot;
                runtimeState.TargetFoundryRoot = Path.Combine(windowsRoot, "Foundry");
                runtimeState.WinReConfigured = true;
                Directory.CreateDirectory(runtimeState.TargetFoundryRoot);
            });

        DeploymentStepResult result = await step.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(DeploymentStepState.Succeeded, result.State);
        Assert.Equal(1, provisioning.CallCount);
        Assert.Contains(processRunner.Calls, call => call.Contains("/Mount-Image", StringComparison.Ordinal));
        Assert.Contains(processRunner.Calls, call => call.Contains("/Unmount-Image", StringComparison.Ordinal) && call.Contains("/Commit", StringComparison.Ordinal));
        Assert.Contains(processRunner.Calls, call => call.Contains("/setbootshelllink", StringComparison.Ordinal));
        Assert.True(File.Exists(Path.Combine(recoveryRoot, "Recovery", "WindowsRE", "FoundryOsRecovery.json")));

        FoundryDeployConfigurationDocument recoveryDeployDocument =
            System.Text.Json.JsonSerializer.Deserialize<FoundryDeployConfigurationDocument>(
                provisioning.CapturedOptions!.DeployConfigurationJson,
                ConfigurationJsonDefaults.SerializerOptions)!;
        FoundryConnectConfigurationDocument recoveryConnectDocument =
            System.Text.Json.JsonSerializer.Deserialize<FoundryConnectConfigurationDocument>(
                provisioning.CapturedOptions.FoundryConnectConfigurationJson,
                ConfigurationJsonDefaults.SerializerOptions)!;

        Assert.False(recoveryDeployDocument.Autopilot.IsEnabled);
        Assert.False(recoveryDeployDocument.Network.ProfileRoaming.IsEnabled);
        Assert.False(recoveryConnectDocument.Wifi.IsEnabled);
        Assert.False(recoveryConnectDocument.Dot1x.IsEnabled);
        Assert.Equal(Path.Combine(Environment.SystemDirectory, "curl.exe"), provisioning.CapturedOptions.CurlExecutableSourcePath);
    }

    private static ProvisionOsRecoveryStep CreateStep(RecordingOsRecoveryPayloadProvisioningService provisioning)
    {
        return new ProvisionOsRecoveryStep(
            provisioning,
            new FakeEmbeddedAssetService(),
            new FakeDeployConfigurationService(),
            new NoOpProcessRunner());
    }

    private static DeploymentStepExecutionContext CreateContext(
        string workspaceRoot,
        bool isOsRecoveryEnabled,
        DeploymentMode mode = DeploymentMode.Iso,
        bool isDryRun = false,
        Action<DeploymentRuntimeState>? configureRuntimeState = null)
    {
        DeploymentContext request = new()
        {
            Mode = mode,
            CacheRootPath = workspaceRoot,
            TargetDiskNumber = 1,
            TargetComputerName = "LAB01",
            OperatingSystem = new OperatingSystemCatalogItem
            {
                Architecture = "x64"
            },
            DriverPackSelectionKind = DriverPackSelectionKind.None,
            OsRecovery = new DeployOsRecoverySettings
            {
                IsEnabled = isOsRecoveryEnabled
            },
            IsDryRun = isDryRun
        };

        DeploymentRuntimeState runtimeState = new()
        {
            WorkspaceRoot = workspaceRoot,
            Mode = mode,
            TargetDiskNumber = 1,
            IsOsRecoveryEnabled = isOsRecoveryEnabled,
            IsDryRun = isDryRun
        };
        configureRuntimeState?.Invoke(runtimeState);

        return new DeploymentStepExecutionContext(
            request,
            runtimeState,
            [DeploymentStepNames.ProvisionOsRecovery],
            new FakeOperationProgressService(),
            new FakeDeploymentLogService(),
            new FakeTargetDiskService(),
            _ => { });
    }

    private sealed class RecordingOsRecoveryPayloadProvisioningService : IOsRecoveryPayloadProvisioningService
    {
        public int CallCount { get; private set; }
        public OsRecoveryPayloadProvisioningOptions? CapturedOptions { get; private set; }

        public Task<WinPeResult<OsRecoveryPayloadProvisioningResult>> ProvisionAsync(
            OsRecoveryPayloadProvisioningOptions options,
            IProgress<WinPeDownloadProgress>? downloadProgress = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            CapturedOptions = options;
            return Task.FromResult(WinPeResult<OsRecoveryPayloadProvisioningResult>.Success(new OsRecoveryPayloadProvisioningResult
            {
                BootMenuConfigurationXml = "<BootShell />",
                ManagedPayloadSizeBytes = 1
            }));
        }
    }

    private sealed class FakeEmbeddedAssetService : IWinPeEmbeddedAssetService
    {
        public string GetBootstrapScriptContent() => "bootstrap";
        public string GetUsbProvisioningScriptTemplateContent() => "usb";
        public string GetIanaWindowsTimeZoneMapJson() => "{}";
        public string GetSevenZipSourceDirectoryPath() => string.Empty;
    }

    private sealed class FakeDeployConfigurationService : IDeployConfigurationService
    {
        private readonly string? _configurationPath;

        public FakeDeployConfigurationService(string? configurationPath = null)
        {
            _configurationPath = configurationPath;
        }

        public DeployConfigurationLoadResult LoadOptional()
        {
            return string.IsNullOrWhiteSpace(_configurationPath)
                ? new DeployConfigurationLoadResult()
                : new DeployConfigurationLoadResult
                {
                    Exists = true,
                    ConfigurationPath = _configurationPath
                };
        }
    }

    private sealed class NoOpProcessRunner : IProcessRunner
    {
        public Task<ProcessExecutionResult> RunAsync(
            string fileName,
            string arguments,
            string workingDirectory,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ProcessExecutionResult { ExitCode = 0 });
        }

        public Task<ProcessExecutionResult> RunAsync(
            string fileName,
            IEnumerable<string> arguments,
            string workingDirectory,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ProcessExecutionResult { ExitCode = 0 });
        }

        public Task<ProcessExecutionResult> RunAsync(
            string fileName,
            IEnumerable<string> arguments,
            string workingDirectory,
            Action<string>? onOutputData,
            Action<string>? onErrorData,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ProcessExecutionResult { ExitCode = 0 });
        }
    }

    private sealed class RecordingProcessRunner : IProcessRunner
    {
        public List<string> Calls { get; } = [];

        public Task<ProcessExecutionResult> RunAsync(
            string fileName,
            string arguments,
            string workingDirectory,
            CancellationToken cancellationToken = default)
        {
            Calls.Add($"{fileName} {arguments}");
            return Task.FromResult(new ProcessExecutionResult { ExitCode = 0, FileName = fileName, Arguments = arguments });
        }

        public Task<ProcessExecutionResult> RunAsync(
            string fileName,
            IEnumerable<string> arguments,
            string workingDirectory,
            CancellationToken cancellationToken = default)
        {
            string joinedArguments = string.Join(' ', arguments);
            Calls.Add($"{fileName} {joinedArguments}");
            return Task.FromResult(new ProcessExecutionResult { ExitCode = 0, FileName = fileName, Arguments = joinedArguments });
        }

        public Task<ProcessExecutionResult> RunAsync(
            string fileName,
            IEnumerable<string> arguments,
            string workingDirectory,
            Action<string>? onOutputData,
            Action<string>? onErrorData,
            CancellationToken cancellationToken = default)
        {
            return RunAsync(fileName, arguments, workingDirectory, cancellationToken);
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

        public void Release(DeploymentLogSession session)
        {
        }
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

    private sealed class TempWorkspace : IDisposable
    {
        private TempWorkspace(string rootPath)
        {
            RootPath = rootPath;
        }

        public string RootPath { get; }

        public static TempWorkspace Create()
        {
            string rootPath = Path.Combine(Path.GetTempPath(), $"foundry-osrecovery-step-{Guid.NewGuid():N}");
            Directory.CreateDirectory(rootPath);
            return new TempWorkspace(rootPath);
        }

        public void Dispose()
        {
            Directory.Delete(RootPath, recursive: true);
        }
    }
}
