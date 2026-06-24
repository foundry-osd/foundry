// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Models;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Autopilot;
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
        ProvisionAutopilotStep step = CreateStep();
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
        ProvisionAutopilotStep step = CreateStep();
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
        var uploadService = new FakeAutopilotHardwareHashUploadService(
            AutopilotHardwareHashUploadResult.Completed("Device imported."));
        ProvisionAutopilotStep step = CreateStep(uploadService: uploadService);
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
        Assert.Equal(AutopilotHardwareHashUploadState.Completed, context.RuntimeState.AutopilotHardwareHashUploadState);
        Assert.Equal("Sales", context.RuntimeState.AutopilotHardwareHashGroupTag);
        Assert.Null(context.RuntimeState.StagedAutopilotConfigurationPath);
        Assert.False(Directory.Exists(Path.Combine(workspace.TargetWindowsRootPath, "Windows", "Provisioning", "Autopilot")));
        Assert.True(File.Exists(Path.Combine(context.RuntimeState.AutopilotHardwareHashDiagnosticsPath!, "autopilot-hash-upload-status.json")));
        Assert.Single(uploadService.Requests);
        Assert.Equal("SER123", uploadService.Requests[0].Identity.SerialNumber);
        Assert.Equal("HASHVALUE", uploadService.Requests[0].Identity.HardwareHash);
        Assert.Equal("Sales", uploadService.Requests[0].Identity.GroupTag);
    }

    [Fact]
    public async Task ExecuteAsync_WhenLiveInteractiveHardwareHashModeIsSelected_StagesRegistrationAssistant()
    {
        using TempDeploymentWorkspace workspace = TempDeploymentWorkspace.Create();
        var uploadService = new FakeAutopilotHardwareHashUploadService(
            AutopilotHardwareHashUploadResult.Completed("Graph should not be called."));
        ProvisionAutopilotStep step = CreateStep(uploadService: uploadService);
        DeploymentStepExecutionContext context = CreateContext(
            workspace,
            isDryRun: false,
            provisioningMode: AutopilotProvisioningMode.InteractiveHardwareHashUpload);

        DeploymentStepResult result = await step.ExecuteAsync(context, CancellationToken.None);

        string registrationRoot = Path.Combine(workspace.TargetWindowsRootPath, "Windows", "Temp", "Foundry", "AutopilotRegistration");
        string expectedConfigPath = Path.Combine(registrationRoot, "config.json");

        Assert.Equal(DeploymentStepState.Succeeded, result.State);
        Assert.Contains("registration assistant", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(AutopilotProvisioningMode.InteractiveHardwareHashUpload, context.RuntimeState.AutopilotProvisioningMode);
        Assert.Equal(AutopilotHardwareHashUploadState.NotPlanned, context.RuntimeState.AutopilotHardwareHashUploadState);
        Assert.Equal(expectedConfigPath, context.RuntimeState.StagedAutopilotConfigurationPath);
        Assert.True(File.Exists(Path.Combine(registrationRoot, "Start-FoundryAutopilotRegistration.ps1")));
        Assert.True(File.Exists(Path.Combine(registrationRoot, "Start-FoundryAutopilotRegistration.cmd")));
        Assert.True(File.Exists(Path.Combine(registrationRoot, "Start-FoundryAutopilotRegistrationOobe.cmd")));
        Assert.True(File.Exists(Path.Combine(registrationRoot, "Wait-FoundryAutopilotRegistrationOobe.ps1")));
        Assert.True(File.Exists(expectedConfigPath));
        Assert.True(File.Exists(Path.Combine(workspace.TargetWindowsRootPath, "Windows", "Setup", "Scripts", "OOBE.cmd")));
        Assert.True(Directory.Exists(Path.Combine(registrationRoot, "State")));
        Assert.True(Directory.Exists(Path.Combine(workspace.TargetWindowsRootPath, "Windows", "Temp", "Foundry", "Logs", "AutopilotRegistration")));
        Assert.False(Directory.Exists(Path.Combine(workspace.TargetWindowsRootPath, "Windows", "Provisioning", "Autopilot")));
        Assert.Empty(uploadService.Requests);
    }

    [Fact]
    public async Task ExecuteAsync_WhenGraphUploadFailsAfterCapture_ContinuesDeployment()
    {
        using TempDeploymentWorkspace workspace = TempDeploymentWorkspace.Create();
        ProvisionAutopilotStep step = CreateStep(
            uploadService: new FakeAutopilotHardwareHashUploadService(
                AutopilotHardwareHashUploadResult.Failed(
                    AutopilotHardwareHashUploadState.UploadFailed,
                    "Autopilot hardware hash upload failed: consent is missing.",
                    "ConsentMissing")));
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
                ActiveCertificateExpiresOnUtc = DateTimeOffset.UtcNow.AddMonths(1)
            });

        DeploymentStepResult result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.NotEqual(DeploymentStepState.Failed, result.State);
        Assert.Equal(DeploymentStepState.Skipped, result.State);
        Assert.Equal(AutopilotHardwareHashUploadState.UploadFailed, context.RuntimeState.AutopilotHardwareHashUploadState);
        Assert.Contains("consent", result.Message, StringComparison.OrdinalIgnoreCase);

        string statusPath = Path.Combine(context.RuntimeState.AutopilotHardwareHashDiagnosticsPath!, "autopilot-hash-upload-status.json");
        using JsonDocument status = JsonDocument.Parse(await File.ReadAllTextAsync(statusPath));
        Assert.Equal("UploadFailed", status.RootElement.GetProperty("uploadState").GetString());
        Assert.Equal("ConsentMissing", status.RootElement.GetProperty("failureCode").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_WhenHardwareHashCaptureReportIsIncomplete_ContinuesDeployment()
    {
        using TempDeploymentWorkspace workspace = TempDeploymentWorkspace.Create();
        ProvisionAutopilotStep step = CreateStep(
            captureService: new FakeAutopilotHardwareHashCaptureService(
                AutopilotHardwareHashCaptureResult.Failed(
                    AutopilotHardwareHashCaptureFailureCode.SerialMissing,
                    "OA3 report does not contain a serial number.")),
            uploadService: new FakeAutopilotHardwareHashUploadService(
                AutopilotHardwareHashUploadResult.Completed("Graph should not be called.")));
        DeploymentStepExecutionContext context = CreateContext(
            workspace,
            isDryRun: false,
            provisioningMode: AutopilotProvisioningMode.HardwareHashUpload,
            hardwareHashUpload: CreateCompleteHardwareHashSettings());

        DeploymentStepResult result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(DeploymentStepState.Skipped, result.State);
        Assert.Equal(AutopilotHardwareHashUploadState.CaptureFailed, context.RuntimeState.AutopilotHardwareHashUploadState);
        Assert.Contains("serial number", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_WhenHardwareHashCaptureCannotLoadPcpKsp_FailsDeployment()
    {
        using TempDeploymentWorkspace workspace = TempDeploymentWorkspace.Create();
        ProvisionAutopilotStep step = CreateStep(
            captureService: new FakeAutopilotHardwareHashCaptureService(
                AutopilotHardwareHashCaptureResult.Failed(
                    AutopilotHardwareHashCaptureFailureCode.SupportLibraryLoadFailed,
                    "Failed to load PCPKsp.dll")));
        DeploymentStepExecutionContext context = CreateContext(
            workspace,
            isDryRun: false,
            provisioningMode: AutopilotProvisioningMode.HardwareHashUpload,
            hardwareHashUpload: CreateCompleteHardwareHashSettings());

        DeploymentStepResult result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(DeploymentStepState.Failed, result.State);
        Assert.Equal(AutopilotHardwareHashUploadState.CaptureFailed, context.RuntimeState.AutopilotHardwareHashUploadState);
        Assert.Contains("PCPKsp", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_WhenLiveHardwareHashModeRunsBeforeTargetWindowsRootExists_FailsBeforePlanningUpload()
    {
        using TempDeploymentWorkspace workspace = TempDeploymentWorkspace.Create();
        ProvisionAutopilotStep step = CreateStep();
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
        ProvisionAutopilotStep step = CreateStep();
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

    [Fact]
    public async Task ExecuteAsync_WhenDryRunInteractiveHardwareHashModeIsSelected_WritesSanitizedManifest()
    {
        using TempDeploymentWorkspace workspace = TempDeploymentWorkspace.Create();
        ProvisionAutopilotStep step = CreateStep();
        DeploymentStepExecutionContext context = CreateContext(
            workspace,
            isDryRun: true,
            provisioningMode: AutopilotProvisioningMode.InteractiveHardwareHashUpload);

        DeploymentStepResult result = await step.ExecuteAsync(context, CancellationToken.None);

        string manifestPath = Path.Combine(workspace.TargetFoundryRootPath, "Autopilot", "interactive-registration.dryrun.json");

        Assert.Equal(DeploymentStepState.Succeeded, result.State);
        Assert.Contains("registration assistant", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(AutopilotProvisioningMode.InteractiveHardwareHashUpload, context.RuntimeState.AutopilotProvisioningMode);
        Assert.Equal(AutopilotHardwareHashUploadState.NotPlanned, context.RuntimeState.AutopilotHardwareHashUploadState);
        Assert.Equal(manifestPath, context.RuntimeState.StagedAutopilotConfigurationPath);
        Assert.True(File.Exists(manifestPath));
        using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
        Assert.Equal("interactiveHardwareHashUpload", manifest.RootElement.GetProperty("provisioningMode").GetString());
        Assert.False(manifest.RootElement.TryGetProperty("certificatePfxSecret", out _));
        Assert.False(manifest.RootElement.TryGetProperty("certificatePfxPasswordSecret", out _));
        Assert.False(manifest.RootElement.TryGetProperty("groupTag", out _));
        Assert.False(File.Exists(Path.Combine(workspace.TargetFoundryRootPath, "Autopilot", "autopilot-profile-stage.dryrun.json")));
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

    private static ProvisionAutopilotStep CreateStep(
        IAutopilotHardwareHashCaptureService? captureService = null,
        IAutopilotHardwareHashUploadService? uploadService = null)
    {
        return new ProvisionAutopilotStep(
            captureService ?? new FakeAutopilotHardwareHashCaptureService(),
            uploadService ?? new FakeAutopilotHardwareHashUploadService(
                AutopilotHardwareHashUploadResult.Completed("Device imported.")),
            new AutopilotInteractiveRegistrationProvisioningService(new SetupCompleteScriptService()));
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

    private sealed class FakeAutopilotHardwareHashCaptureService(
        AutopilotHardwareHashCaptureResult? result = null) : IAutopilotHardwareHashCaptureService
    {
        public Task<AutopilotHardwareHashCaptureResult> CaptureAsync(
            AutopilotHardwareHashCaptureRequest request,
            CancellationToken cancellationToken = default)
        {
            if (result is not null)
            {
                return Task.FromResult(result);
            }

            Directory.CreateDirectory(request.DiagnosticsRootPath);
            string oa3XmlPath = Path.Combine(request.DiagnosticsRootPath, "OA3.xml");
            string oa3LogPath = Path.Combine(request.DiagnosticsRootPath, "OA3.log");
            string csvPath = Path.Combine(request.DiagnosticsRootPath, "AutopilotHWID.csv");
            File.WriteAllText(oa3XmlPath, "<Key />");
            File.WriteAllText(oa3LogPath, "oa3");
            File.WriteAllText(csvPath, "csv");
            AutopilotHardwareHashDeviceIdentity identity = new("SER123", "HASHVALUE", request.GroupTag);
            return Task.FromResult(AutopilotHardwareHashCaptureResult.Succeeded(identity, oa3XmlPath, oa3LogPath, csvPath));
        }
    }

    private sealed class FakeAutopilotHardwareHashUploadService(AutopilotHardwareHashUploadResult result) : IAutopilotHardwareHashUploadService
    {
        public List<AutopilotHardwareHashUploadRequest> Requests { get; } = [];

        public Task<AutopilotHardwareHashUploadResult> UploadAsync(
            AutopilotHardwareHashUploadRequest request,
            IProgress<AutopilotHardwareHashUploadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(result);
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
