using System.Diagnostics;
using System.IO;
using Foundry.Deploy.Models;
using Foundry.Deploy.Services.Hardware;
using Foundry.Deploy.Services.Logging;
using Foundry.Deploy.Services.Operations;
using Foundry.Telemetry;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.Services.Deployment;

/// <summary>
/// Runs the deployment workflow in the registered step order and persists progress/log state.
/// </summary>
public sealed class DeploymentOrchestrator : IDeploymentOrchestrator
{
    private readonly IOperationProgressService _operationProgressService;
    private readonly IDeploymentLogService _deploymentLogService;
    private readonly ITargetDiskService _targetDiskService;
    private readonly IReadOnlyList<IDeploymentStep> _steps;
    private readonly ITelemetryService _telemetryService;
    private readonly ILogger<DeploymentOrchestrator> _logger;

    /// <summary>
    /// Initializes the deployment orchestrator and validates the registered step sequence.
    /// </summary>
    public DeploymentOrchestrator(
        IOperationProgressService operationProgressService,
        IDeploymentLogService deploymentLogService,
        ITargetDiskService targetDiskService,
        IEnumerable<IDeploymentStep> steps,
        ITelemetryService telemetryService,
        ILogger<DeploymentOrchestrator> logger)
    {
        _operationProgressService = operationProgressService;
        _deploymentLogService = deploymentLogService;
        _targetDiskService = targetDiskService;
        _telemetryService = telemetryService;
        _logger = logger;

        _steps = steps
            .OrderBy(step => step.Order)
            .ToArray();

        if (_steps.Count != DeploymentStepNames.All.Count)
        {
            throw new InvalidOperationException(
                $"Expected {DeploymentStepNames.All.Count} deployment steps but found {_steps.Count}.");
        }

        PlannedSteps = _steps
            .Select(step => step.Name)
            .ToArray();

        if (!PlannedSteps.SequenceEqual(DeploymentStepNames.All))
        {
            throw new InvalidOperationException("The registered deployment steps do not match the expected workflow.");
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> PlannedSteps { get; }

    /// <inheritdoc />
    public event EventHandler<DeploymentStepProgress>? StepProgressChanged;

    /// <inheritdoc />
    public async Task<DeploymentResult> RunAsync(DeploymentContext context, CancellationToken cancellationToken = default)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        _logger.LogInformation(
            "Starting deployment orchestration. Mode={Mode}, IsDryRun={IsDryRun}, TargetDiskNumber={TargetDiskNumber}, TargetComputerName={TargetComputerName}, DriverPackSelectionKind={DriverPackSelectionKind}, ApplyFirmwareUpdates={ApplyFirmwareUpdates}",
            context.Mode,
            context.IsDryRun,
            context.TargetDiskNumber,
            context.TargetComputerName,
            context.DriverPackSelectionKind,
            context.ApplyFirmwareUpdates);

        if (!_operationProgressService.TryStart(OperationKind.Deploy, "Starting Foundry.Deploy orchestration.", 0))
        {
            _logger.LogWarning("Deployment orchestration rejected because another operation is already in progress.");
            await TrackDeploymentCompletedAsync(
                context,
                runtimeState: null,
                success: false,
                cancelled: false,
                failedStepName: "operation_busy",
                stopwatch.Elapsed,
                CancellationToken.None).ConfigureAwait(false);

            return new DeploymentResult
            {
                IsSuccess = false,
                Message = "Another operation is already running.",
                LogsDirectoryPath = string.Empty
            };
        }

        var runtimeState = new DeploymentRuntimeState
        {
            WorkspaceRoot = DeploymentStepExecutionContext.ResolveWorkspaceRoot(context),
            Mode = context.Mode,
            IsDryRun = context.IsDryRun,
            RequestedCacheRootPath = context.CacheRootPath,
            TargetDiskNumber = context.TargetDiskNumber,
            TargetComputerName = context.TargetComputerName,
            OperatingSystemFileName = context.OperatingSystem.FileName,
            OperatingSystemUrl = context.OperatingSystem.Url,
            DriverPackSelectionKind = context.DriverPackSelectionKind,
            DriverPackName = context.DriverPack?.DisplayLabel,
            DriverPackUrl = context.DriverPack?.DownloadUrl,
            ApplyFirmwareUpdates = context.ApplyFirmwareUpdates,
            IsAutopilotEnabled = context.IsAutopilotEnabled,
            SelectedAutopilotProfileFolderName = context.SelectedAutopilotProfile?.FolderName,
            SelectedAutopilotProfileDisplayName = context.SelectedAutopilotProfile?.DisplayName
        };

        DeploymentStepExecutionContext? executionContext = null;

        try
        {
            _logger.LogInformation("Deployment workspace root resolved to '{WorkspaceRoot}'.", runtimeState.WorkspaceRoot);
            executionContext = new DeploymentStepExecutionContext(
                context,
                runtimeState,
                PlannedSteps,
                _operationProgressService,
                _deploymentLogService,
                _targetDiskService,
                progress => StepProgressChanged?.Invoke(this, progress));

            for (int i = 0; i < _steps.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                IDeploymentStep step = _steps[i];
                executionContext.SetCurrentStep(step, i + 1);

                _logger.LogInformation(
                    "Executing deployment step {StepIndex}/{StepCount}: {StepName}",
                    i + 1,
                    _steps.Count,
                    step.Name);

                executionContext.EmitCurrentStep(
                    DeploymentStepState.Running,
                    $"Starting {step.Name}.",
                    stepSubProgressIndeterminate: true,
                    stepSubProgressLabel: $"Starting {step.Name}...");
                await executionContext.AppendLogAsync(DeploymentLogLevel.Info, $"[STEP] {step.Name}", cancellationToken).ConfigureAwait(false);

                DeploymentStepResult result = await step.ExecuteAsync(executionContext, cancellationToken).ConfigureAwait(false);

                _operationProgressService.Report(CalculateOverallProgressPercent(i + 1), result.Message);
                executionContext.EmitCurrentStep(
                    result.State,
                    result.Message,
                    stepSubProgressPercent: result.State == DeploymentStepState.Succeeded ? 100 : null,
                    stepSubProgressIndeterminate: result.State != DeploymentStepState.Succeeded,
                    stepSubProgressLabel: result.Message);

                if (result.State == DeploymentStepState.Failed)
                {
                    _logger.LogWarning("Deployment step failed. StepName={StepName}, Message={Message}", step.Name, result.Message);
                    throw new InvalidOperationException(result.Message);
                }

                if (result.State == DeploymentStepState.Succeeded)
                {
                    runtimeState.CompletedSteps.Add(step.Name);
                }

                await executionContext.SaveRuntimeStateAsync(cancellationToken).ConfigureAwait(false);
            }

            _operationProgressService.Complete("Deployment orchestration completed.");
            _logger.LogInformation("Deployment orchestration completed successfully.");
            await TrackDeploymentCompletedAsync(
                context,
                runtimeState,
                success: true,
                cancelled: false,
                failedStepName: null,
                stopwatch.Elapsed,
                CancellationToken.None).ConfigureAwait(false);

            return new DeploymentResult
            {
                IsSuccess = true,
                Message = "Deployment orchestration completed.",
                LogsDirectoryPath = ResolveLogsDirectory(executionContext)
            };
        }
        catch (OperationCanceledException)
        {
            _operationProgressService.Fail("Deployment cancelled.");
            _logger.LogWarning("Deployment orchestration cancelled.");
            if (executionContext is not null)
            {
                await TryRebindLogsToFinalTargetAsync(executionContext, CancellationToken.None).ConfigureAwait(false);
                await executionContext
                    .AppendLogAsync(DeploymentLogLevel.Warning, "[WARN] Deployment cancelled by user.", CancellationToken.None)
                    .ConfigureAwait(false);
            }
            await TrackDeploymentCompletedAsync(
                context,
                runtimeState,
                success: false,
                cancelled: true,
                failedStepName: ResolveFailedStepName(runtimeState),
                stopwatch.Elapsed,
                CancellationToken.None).ConfigureAwait(false);

            return new DeploymentResult
            {
                IsSuccess = false,
                Message = "Deployment cancelled.",
                LogsDirectoryPath = ResolveLogsDirectory(executionContext, runtimeState)
            };
        }
        catch (Exception ex)
        {
            _operationProgressService.Fail("Deployment failed.");
            _logger.LogError(ex, "Deployment orchestration failed.");
            if (executionContext is not null)
            {
                await TryRebindLogsToFinalTargetAsync(executionContext, CancellationToken.None).ConfigureAwait(false);
                await executionContext
                    .AppendLogAsync(DeploymentLogLevel.Error, $"[ERROR] {ex}", CancellationToken.None)
                    .ConfigureAwait(false);
            }
            await TrackDeploymentCompletedAsync(
                context,
                runtimeState,
                success: false,
                cancelled: false,
                failedStepName: ResolveFailedStepName(runtimeState),
                stopwatch.Elapsed,
                CancellationToken.None).ConfigureAwait(false);

            return new DeploymentResult
            {
                IsSuccess = false,
                Message = ex.Message,
                LogsDirectoryPath = ResolveLogsDirectory(executionContext, runtimeState)
            };
        }
    }

    private Task TrackDeploymentCompletedAsync(
        DeploymentContext context,
        DeploymentRuntimeState? runtimeState,
        bool success,
        bool cancelled,
        string? failedStepName,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        HardwareProfile? hardware = runtimeState?.HardwareProfile;
        var properties = new Dictionary<string, object?>
            {
                ["success"] = success,
                ["cancelled"] = cancelled,
                ["duration_seconds"] = Math.Round(duration.TotalSeconds, 2),
                ["completed_step_count"] = runtimeState?.CompletedSteps.Count ?? 0,
                ["failed_step_name"] = failedStepName,
                ["mode"] = context.Mode.ToString().ToLowerInvariant(),
                ["is_dry_run"] = context.IsDryRun,
                ["hardware_vendor"] = NormalizeTelemetryString(hardware?.Manufacturer),
                ["hardware_model"] = NormalizeTelemetryString(hardware?.Model),
                ["is_virtual_machine"] = hardware?.IsVirtualMachine ?? false,
                ["os_product"] = ResolveOperatingSystemProduct(context.OperatingSystem),
                ["os_version"] = NormalizeTelemetryString(context.OperatingSystem.ReleaseId),
                ["os_build"] = NormalizeTelemetryString(context.OperatingSystem.Build),
                ["os_architecture"] = NormalizeTelemetryString(context.OperatingSystem.Architecture),
                ["os_language"] = NormalizeTelemetryString(context.OperatingSystem.LanguageCode),
                ["driver_pack_selection_kind"] = context.DriverPackSelectionKind.ToString().ToLowerInvariant(),
                ["driver_pack_vendor"] = NormalizeTelemetryString(context.DriverPack?.Manufacturer, "none"),
                ["driver_pack_model"] = ResolveDriverPackCatalogModel(context.DriverPack),
                ["firmware_updates_enabled"] = context.ApplyFirmwareUpdates,
                ["autopilot_enabled"] = context.IsAutopilotEnabled
            };

        _logger.LogDebug(
            "Tracking deployment telemetry event. Success={Success}, Cancelled={Cancelled}, DurationSeconds={DurationSeconds}, CompletedStepCount={CompletedStepCount}, FailedStepName={FailedStepName}, Mode={Mode}, IsDryRun={IsDryRun}, HardwareVendor={HardwareVendor}, HardwareModel={HardwareModel}, OsProduct={OsProduct}, OsVersion={OsVersion}, DriverPackSelectionKind={DriverPackSelectionKind}, DriverPackVendor={DriverPackVendor}, DriverPackModel={DriverPackModel}.",
            success,
            cancelled,
            properties["duration_seconds"],
            properties["completed_step_count"],
            failedStepName,
            properties["mode"],
            context.IsDryRun,
            properties["hardware_vendor"],
            properties["hardware_model"],
            properties["os_product"],
            properties["os_version"],
            properties["driver_pack_selection_kind"],
            properties["driver_pack_vendor"],
            properties["driver_pack_model"]);

        return _telemetryService.TrackAsync("deployment_completed", properties, cancellationToken);
    }

    private static string ResolveFailedStepName(DeploymentRuntimeState runtimeState)
    {
        return string.IsNullOrWhiteSpace(runtimeState.CurrentStep)
            ? "unknown"
            : runtimeState.CurrentStep;
    }

    private static string ResolveOperatingSystemProduct(OperatingSystemCatalogItem operatingSystem)
    {
        return string.IsNullOrWhiteSpace(operatingSystem.WindowsRelease)
            ? "windows"
            : $"windows_{NormalizeTelemetryString(operatingSystem.WindowsRelease)}";
    }

    private static string NormalizeTelemetryString(string? value, string fallback = "unknown")
    {
        return string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim().ToLowerInvariant();
    }

    private static string ResolveDriverPackCatalogModel(DriverPackCatalogItem? driverPack)
    {
        if (driverPack is null)
        {
            return "none";
        }

        string? model = driverPack.ModelNames.FirstOrDefault(modelName => !string.IsNullOrWhiteSpace(modelName));
        return NormalizeTelemetryString(model);
    }

    private int CalculateOverallProgressPercent(int stepIndex)
    {
        return (int)Math.Round((double)stepIndex / _steps.Count * 100d);
    }

    private static async Task TryRebindLogsToFinalTargetAsync(
        DeploymentStepExecutionContext executionContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(executionContext.RuntimeState.TargetWindowsPartitionRoot))
        {
            return;
        }

        string finalRoot = Path.Combine(executionContext.RuntimeState.TargetWindowsPartitionRoot, "Windows", "Temp", "Foundry");
        if (executionContext.LogSession.RootPath.Equals(finalRoot, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            await executionContext.RebindLogSessionToTargetAsync(finalRoot, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Keep the original log session available if final target log relocation fails.
        }
    }

    private static string ResolveLogsDirectory(
        DeploymentStepExecutionContext? executionContext,
        DeploymentRuntimeState? runtimeState = null)
    {
        if (executionContext is not null &&
            !string.IsNullOrWhiteSpace(executionContext.LogSession.LogsDirectoryPath))
        {
            return executionContext.LogSession.LogsDirectoryPath;
        }

        DeploymentRuntimeState? effectiveRuntimeState = executionContext?.RuntimeState ?? runtimeState;
        if (!string.IsNullOrWhiteSpace(effectiveRuntimeState?.TargetWindowsPartitionRoot))
        {
            return Path.Combine(effectiveRuntimeState.TargetWindowsPartitionRoot, "Windows", "Temp", "Foundry", "Logs");
        }

        return executionContext?.ResolveWorkspaceLogsPath() ?? string.Empty;
    }
}
