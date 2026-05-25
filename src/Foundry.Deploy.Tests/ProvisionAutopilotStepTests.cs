using Foundry.Deploy.Models;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.Deployment.Steps;
using Foundry.Deploy.Services.Hardware;
using Foundry.Deploy.Services.Logging;
using Foundry.Deploy.Services.Operations;
using System.Text.Json;

namespace Foundry.Deploy.Tests;

public sealed class ProvisionAutopilotStepTests
{
    [Fact]
    public async Task ExecuteAsync_WhenLiveJsonMode_StagesAutopilotProfile()
    {
        using TempDeploymentWorkspace workspace = TempDeploymentWorkspace.Create();
        string sourceConfigurationPath = Path.Combine(workspace.RootPath, "AutopilotConfigurationFile.json");
        await File.WriteAllTextAsync(sourceConfigurationPath, """{"profile":true}""");
        ProvisionAutopilotStep step = new();
        DeploymentStepExecutionContext context = CreateContext(
            workspace,
            isDryRun: false,
            provisioningMode: AutopilotProvisioningMode.JsonProfile,
            selectedProfile: new AutopilotProfileCatalogItem
            {
                FolderName = "Corporate",
                DisplayName = "Corporate",
                ConfigurationFilePath = sourceConfigurationPath
            });

        DeploymentStepResult result = await step.ExecuteAsync(context, CancellationToken.None);

        string expectedPath = Path.Combine(workspace.TargetWindowsRootPath, "Windows", "Provisioning", "Autopilot", "AutopilotConfigurationFile.json");
        Assert.Equal(DeploymentStepState.Succeeded, result.State);
        Assert.True(File.Exists(expectedPath));
        Assert.Equal(expectedPath, context.RuntimeState.StagedAutopilotConfigurationPath);
        Assert.Equal(AutopilotProvisioningMode.JsonProfile, context.RuntimeState.AutopilotProvisioningMode);
        Assert.Equal(AutopilotHardwareHashUploadState.NotPlanned, context.RuntimeState.AutopilotHardwareHashUploadState);
    }

    [Fact]
    public async Task ExecuteAsync_WhenLiveHardwareHashCertificateIsExpired_SkipsUpload()
    {
        using TempDeploymentWorkspace workspace = TempDeploymentWorkspace.Create();
        ProvisionAutopilotStep step = new();
        DeploymentStepExecutionContext context = CreateContext(
            workspace,
            isDryRun: false,
            provisioningMode: AutopilotProvisioningMode.HardwareHashUpload,
            hardwareHashUpload: new DeployAutopilotHardwareHashUploadSettings
            {
                ActiveCertificateExpiresOnUtc = DateTimeOffset.UtcNow.AddDays(-1)
            });

        DeploymentStepResult result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(DeploymentStepState.Skipped, result.State);
        Assert.Contains("expired", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(AutopilotHardwareHashUploadState.SkippedCertificateExpired, context.RuntimeState.AutopilotHardwareHashUploadState);
        Assert.True(Directory.Exists(context.RuntimeState.AutopilotHardwareHashDiagnosticsPath));
        Assert.Contains(
            "autopilot-hash-upload-status.json",
            Directory.EnumerateFiles(context.RuntimeState.AutopilotHardwareHashDiagnosticsPath)
                .Select(Path.GetFileName));
    }

    [Fact]
    public async Task ExecuteAsync_WhenLiveHardwareHashModeIsConfigured_RecordsPlannedUploadWithoutJsonStaging()
    {
        using TempDeploymentWorkspace workspace = TempDeploymentWorkspace.Create();
        ProvisionAutopilotStep step = new();
        DeploymentStepExecutionContext context = CreateContext(
            workspace,
            isDryRun: false,
            provisioningMode: AutopilotProvisioningMode.HardwareHashUpload,
            hardwareHashUpload: new DeployAutopilotHardwareHashUploadSettings
            {
                TenantId = "tenant-id",
                ClientId = "client-id",
                ActiveCertificateKeyId = "key-id",
                ActiveCertificateThumbprint = "ABCDEF123456",
                ActiveCertificateExpiresOnUtc = DateTimeOffset.UtcNow.AddMonths(1),
                DefaultGroupTag = "Sales"
            });

        DeploymentStepResult result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(DeploymentStepState.Succeeded, result.State);
        Assert.Equal(AutopilotHardwareHashUploadState.PendingHashCapture, context.RuntimeState.AutopilotHardwareHashUploadState);
        Assert.Equal("Sales", context.RuntimeState.AutopilotHardwareHashGroupTag);
        Assert.Null(context.RuntimeState.StagedAutopilotConfigurationPath);
        Assert.False(Directory.Exists(Path.Combine(workspace.TargetWindowsRootPath, "Windows", "Provisioning", "Autopilot")));
        Assert.True(File.Exists(Path.Combine(context.RuntimeState.AutopilotHardwareHashDiagnosticsPath!, "autopilot-hash-upload-status.json")));
    }

    [Fact]
    public async Task ExecuteAsync_WhenLiveHardwareHashModeRunsBeforeTargetWindowsRootExists_FailsBeforePlanningUpload()
    {
        using TempDeploymentWorkspace workspace = TempDeploymentWorkspace.Create();
        ProvisionAutopilotStep step = new();
        DeploymentStepExecutionContext context = CreateContext(
            workspace,
            isDryRun: false,
            provisioningMode: AutopilotProvisioningMode.HardwareHashUpload,
            hardwareHashUpload: CreateCompleteHardwareHashSettings());
        context.RuntimeState.TargetWindowsPartitionRoot = null;

        DeploymentStepResult result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(DeploymentStepState.Failed, result.State);
        Assert.Contains("Target Windows partition", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(AutopilotHardwareHashUploadState.Planned, context.RuntimeState.AutopilotHardwareHashUploadState);
    }

    [Fact]
    public async Task ExecuteAsync_WhenDryRunHardwareHashMode_WritesSanitizedHashManifest()
    {
        using TempDeploymentWorkspace workspace = TempDeploymentWorkspace.Create();
        ProvisionAutopilotStep step = new();
        DeploymentStepExecutionContext context = CreateContext(
            workspace,
            isDryRun: true,
            provisioningMode: AutopilotProvisioningMode.HardwareHashUpload,
            hardwareHashUpload: new DeployAutopilotHardwareHashUploadSettings
            {
                TenantId = "tenant-id",
                ClientId = "client-id",
                ActiveCertificateKeyId = "key-id",
                ActiveCertificateThumbprint = "ABCDEF123456",
                ActiveCertificateExpiresOnUtc = DateTimeOffset.UtcNow.AddMonths(1),
                DefaultGroupTag = "Sales"
            });

        DeploymentStepResult result = await step.ExecuteAsync(context, CancellationToken.None);

        string manifestPath = Path.Combine(workspace.TargetFoundryRootPath, "Autopilot", "autopilot-hash-upload.dryrun.json");
        Assert.Equal(DeploymentStepState.Succeeded, result.State);
        Assert.True(File.Exists(manifestPath));
        Assert.Equal(AutopilotHardwareHashUploadState.DryRunPrepared, context.RuntimeState.AutopilotHardwareHashUploadState);
        using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
        Assert.Equal("hardwareHashUpload", manifest.RootElement.GetProperty("provisioningMode").GetString());
        Assert.False(manifest.RootElement.TryGetProperty("certificatePfxSecret", out _));
        Assert.False(manifest.RootElement.TryGetProperty("certificatePfxPasswordSecret", out _));
    }

    private static DeployAutopilotHardwareHashUploadSettings CreateCompleteHardwareHashSettings()
    {
        return new DeployAutopilotHardwareHashUploadSettings
        {
            TenantId = "tenant-id",
            ClientId = "client-id",
            ActiveCertificateKeyId = "key-id",
            ActiveCertificateThumbprint = "ABCDEF123456",
            ActiveCertificateExpiresOnUtc = DateTimeOffset.UtcNow.AddMonths(1)
        };
    }

    private static DeploymentStepExecutionContext CreateContext(
        TempDeploymentWorkspace workspace,
        bool isDryRun,
        AutopilotProvisioningMode provisioningMode,
        DeployAutopilotHardwareHashUploadSettings? hardwareHashUpload = null,
        AutopilotProfileCatalogItem? selectedProfile = null)
    {
        DeploymentContext request = new()
        {
            Mode = DeploymentMode.Iso,
            IsDryRun = isDryRun,
            CacheRootPath = workspace.RootPath,
            TargetDiskNumber = 1,
            TargetComputerName = "LAB01",
            OperatingSystem = new OperatingSystemCatalogItem(),
            DriverPackSelectionKind = DriverPackSelectionKind.None,
            IsAutopilotEnabled = true,
            AutopilotProvisioningMode = provisioningMode,
            SelectedAutopilotProfile = selectedProfile,
            AutopilotHardwareHashUpload = hardwareHashUpload ?? new DeployAutopilotHardwareHashUploadSettings()
        };
        DeploymentRuntimeState runtimeState = new()
        {
            WorkspaceRoot = workspace.RootPath,
            TargetWindowsPartitionRoot = workspace.TargetWindowsRootPath,
            TargetFoundryRoot = workspace.TargetFoundryRootPath
        };

        return new DeploymentStepExecutionContext(
            request,
            runtimeState,
            [DeploymentStepNames.ProvisionAutopilot],
            new FakeOperationProgressService(),
            new FakeDeploymentLogService(),
            new FakeTargetDiskService(),
            _ => { });
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
            return File.AppendAllTextAsync(session.LogFilePath, $"{level}: {message}{Environment.NewLine}", cancellationToken);
        }

        public Task SaveStateAsync<TState>(DeploymentLogSession session, TState state, CancellationToken cancellationToken = default)
        {
            return File.WriteAllTextAsync(session.StateFilePath, "{}", cancellationToken);
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

    private sealed class TempDeploymentWorkspace : IDisposable
    {
        private TempDeploymentWorkspace(string rootPath)
        {
            RootPath = rootPath;
            TargetWindowsRootPath = Path.Combine(rootPath, "TargetWindows");
            TargetFoundryRootPath = Path.Combine(rootPath, "TargetFoundry");
            Directory.CreateDirectory(TargetWindowsRootPath);
            Directory.CreateDirectory(TargetFoundryRootPath);
        }

        public string RootPath { get; }
        public string TargetWindowsRootPath { get; }
        public string TargetFoundryRootPath { get; }

        public static TempDeploymentWorkspace Create()
        {
            string rootPath = Path.Combine(Path.GetTempPath(), $"foundry-autopilot-step-{Guid.NewGuid():N}");
            Directory.CreateDirectory(rootPath);
            return new TempDeploymentWorkspace(rootPath);
        }

        public void Dispose()
        {
            Directory.Delete(RootPath, recursive: true);
        }
    }
}
