// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Globalization;
using System.IO;
using Foundry.Core.Services.Telemetry;
using Foundry.Deploy.Models;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Hardware;
using Foundry.Deploy.Services.Network;
using Foundry.Deploy.Services.Http;
using Foundry.Deploy.Services.Logging;
using Foundry.Deploy.Services.Operations;
using Foundry.Telemetry;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.Services.Deployment;

/// <summary>
/// Runs the applicable deployment plan and persists observed outcomes and diagnostic state.
/// </summary>
public sealed class DeploymentOrchestrator : IDeploymentOrchestrator
{
    private readonly IOperationProgressService _operationProgressService;
    private readonly IDeploymentLogService _deploymentLogService;
    private readonly ITargetDiskService _targetDiskService;
    private readonly IReadOnlyDictionary<string, IDeploymentStep> _steps;
    private readonly ITelemetryService _telemetryService;
    private readonly ILogger<DeploymentOrchestrator> _logger;
    private readonly INetworkProfileRoamingArtifactService? _networkProfileRoamingArtifactService;

    /// <summary>
    /// Initializes the deployment orchestrator and validates the registered step sequence.
    /// </summary>
    public DeploymentOrchestrator(
        IOperationProgressService operationProgressService,
        IDeploymentLogService deploymentLogService,
        ITargetDiskService targetDiskService,
        IEnumerable<IDeploymentStep> steps,
        ITelemetryService telemetryService,
        ILogger<DeploymentOrchestrator> logger,
        INetworkProfileRoamingArtifactService? networkProfileRoamingArtifactService = null)
    {
        _operationProgressService = operationProgressService;
        _deploymentLogService = deploymentLogService;
        _targetDiskService = targetDiskService;
        _telemetryService = telemetryService;
        _logger = logger;
        _networkProfileRoamingArtifactService = networkProfileRoamingArtifactService;

        var stepsByName = new Dictionary<string, IDeploymentStep>(StringComparer.Ordinal);
        foreach (IDeploymentStep step in steps)
        {
            if (!stepsByName.TryAdd(step.Name, step))
            {
                throw new InvalidOperationException($"Duplicate deployment step registration: '{step.Name}'.");
            }
        }

        string[] missingSteps = DeploymentStepNames.ExecutionOrder
            .Where(stepName => !stepsByName.ContainsKey(stepName))
            .ToArray();
        string[] unexpectedSteps = stepsByName.Keys
            .Where(stepName => !DeploymentStepNames.ExecutionOrder.Contains(stepName, StringComparer.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        if (missingSteps.Length > 0 || unexpectedSteps.Length > 0)
        {
            throw new InvalidOperationException(
                $"The registered deployment steps do not match the expected workflow. Missing: {FormatStepNames(missingSteps)}. Unexpected: {FormatStepNames(unexpectedSteps)}.");
        }

        _steps = stepsByName;
    }

    /// <inheritdoc />
    public event EventHandler<DeploymentStepProgress>? StepProgressChanged;

    /// <inheritdoc />
    public event EventHandler? CompletionStarting;

    private static string FormatStepNames(IReadOnlyCollection<string> stepNames)
    {
        return stepNames.Count == 0 ? "none" : string.Join(", ", stepNames);
    }

    /// <inheritdoc />
    public async Task<DeploymentResult> RunAsync(DeploymentContext context, CancellationToken cancellationToken = default)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        string operationId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        using IDisposable? logScope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["OperationId"] = operationId,
            ["Workflow"] = "deployment"
        });
        _logger.LogInformation(
            "Starting deployment orchestration. Mode={Mode}, IsDryRun={IsDryRun}, TargetDiskNumber={TargetDiskNumber}, HasTargetComputerName={HasTargetComputerName}, DriverPackSelectionKind={DriverPackSelectionKind}, ApplyFirmwareUpdates={ApplyFirmwareUpdates}",
            context.Mode,
            context.IsDryRun,
            context.TargetDiskNumber,
            !string.IsNullOrWhiteSpace(context.TargetComputerName),
            context.DriverPackSelectionKind,
            context.ApplyFirmwareUpdates);

        if (!_operationProgressService.TryStart(OperationKind.Deploy, "Starting Foundry.Deploy orchestration.", 0))
        {
            LogTerminalOutcome(
                LogLevel.Warning,
                exception: null,
                operationId,
                outcome: "failed",
                context,
                runtimeState: null,
                failedStepName: "operation_busy",
                failure: new DeploymentFailure(
                    DeploymentOperationNames.AcquireGate,
                    DeploymentFailureKinds.Busy,
                    DeploymentFailureReasons.OperationBusy),
                stopwatch.Elapsed,
                cancelled: false);
            await TrackDeploymentCompletedAsync(
                operationId,
                context,
                runtimeState: null,
                success: false,
                cancelled: false,
                failedStepName: "operation_busy",
                failure: new DeploymentFailure(
                    DeploymentOperationNames.AcquireGate,
                    DeploymentFailureKinds.Busy,
                    DeploymentFailureReasons.OperationBusy),
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
            OperationId = operationId,
            WorkspaceRoot = DeploymentStepExecutionContext.ResolveWorkspaceRoot(context),
            Mode = context.Mode,
            IsDryRun = context.IsDryRun,
            RequestedCacheRootPath = context.CacheRootPath,
            TargetDiskNumber = context.TargetDiskNumber,
            TargetComputerName = context.TargetComputerName,
            OperatingSystemFileName = context.OperatingSystem.FileName,
            OperatingSystemUrl = (context.OperatingSystem as OperatingSystemCatalogItem)?.Url ?? string.Empty,
            DriverPackSelectionKind = context.DriverPackSelectionKind,
            DriverPackName = context.DriverPack?.DisplayLabel,
            DriverPackUrl = context.DriverPack?.DownloadUrl,
            ApplyFirmwareUpdates = context.ApplyFirmwareUpdates,
            IsAutopilotEnabled = context.IsAutopilotEnabled,
            AutopilotProvisioningMode = context.AutopilotProvisioningMode,
            SelectedAutopilotProfileFolderName = context.SelectedAutopilotProfile?.FolderName,
            SelectedAutopilotProfileDisplayName = context.SelectedAutopilotProfile?.DisplayName,
            AutopilotHardwareHashGroupTag = context.AutopilotProvisioningMode == AutopilotProvisioningMode.HardwareHashUpload
                ? NormalizeOptionalString(context.AutopilotHardwareHashUpload.DefaultGroupTag)
                : null,
            AutopilotHardwareHashUploadState = context.IsAutopilotEnabled && context.AutopilotProvisioningMode == AutopilotProvisioningMode.HardwareHashUpload
                ? AutopilotHardwareHashUploadState.Planned
                : AutopilotHardwareHashUploadState.NotPlanned,
            Network = context.Network,
            Oobe = context.Oobe,
            AppxRemoval = context.AppxRemoval,
            AiComponentRemoval = context.AiComponentRemoval,
            WindowsOptionalFeatures = context.WindowsOptionalFeatures
        };

        DeploymentStepExecutionContext? executionContext = null;
        bool isCancellationDeferred = false;
        bool isCompletionStarting = false;
        bool succeeded = false;
        string terminalStatus = "Deployment failed.";

        void BeginCompletion()
        {
            if (isCompletionStarting)
            {
                return;
            }

            isCompletionStarting = true;
            CompletionStarting?.Invoke(this, EventArgs.Empty);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<DeploymentPlanEntry> plan = DeploymentPlan.Build(context, runtimeState);
            runtimeState.DriverPackInstallMode = DeploymentPlan.ResolveDriverMode(context);
            _logger.LogInformation("Deployment workspace root resolved to '{WorkspaceRoot}'.", runtimeState.WorkspaceRoot);
            executionContext = new DeploymentStepExecutionContext(
                context,
                runtimeState,
                plan.Select(entry => entry.Name).ToArray(),
                _operationProgressService,
                _deploymentLogService,
                _targetDiskService,
                progress => StepProgressChanged?.Invoke(this, progress));
            await DeploymentRunContextLogger.AppendRunContextAsync(executionContext, cancellationToken).ConfigureAwait(false);

            for (int i = 0; i < plan.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                executionContext.UpdatePlan(plan);
                IDeploymentStep step = _steps[plan[i].Name];
                executionContext.SetCurrentStep(step, i + 1);
                using IDisposable? stepScope = _logger.BeginScope(new Dictionary<string, object?>
                {
                    ["StepName"] = step.Name,
                    ["StepIndex"] = i + 1,
                    ["StepCount"] = plan.Count
                });
                await executionContext.TrySaveRuntimeStateAsync(cancellationToken).ConfigureAwait(false);

                _logger.LogInformation(
                    "Executing deployment step {StepIndex}/{StepCount}: {StepName}.",
                    i + 1,
                    plan.Count,
                    step.Name);

                executionContext.EmitCurrentStep(
                    DeploymentStepState.Running,
                    $"Starting {step.Name}.",
                    stepSubProgressIndeterminate: true,
                    stepSubProgressLabel: $"Starting {step.Name}...");
                cancellationToken.ThrowIfCancellationRequested();
                isCancellationDeferred = !context.IsDryRun && !CanCancelActiveStep(step.Name);
                CancellationToken stepToken = isCancellationDeferred ? CancellationToken.None : cancellationToken;
                DeploymentStepResult result = step.Name == DeploymentStepNames.ValidateTargetConfiguration
                    ? await ExecuteSetupAsync(executionContext, stepToken).ConfigureAwait(false)
                    : await step.ExecuteAsync(executionContext, stepToken).ConfigureAwait(false);
                isCancellationDeferred = false;
                _logger.LogInformation(
                    "Deployment step finished. StepName={StepName}, StepState={StepState}, CurrentOperation={CurrentOperation}",
                    step.Name,
                    result.State,
                    runtimeState.CurrentOperation);

                executionContext.EmitCurrentStep(
                    result.State,
                    result.Message,
                    stepSubProgressPercent: result.State == DeploymentStepState.Succeeded ? 100 : null,
                    stepSubProgressIndeterminate: false,
                    stepSubProgressLabel: result.Message);
                executionContext.ReportCurrentStepProgress(result.Message);
                runtimeState.StepOutcomes.Add(new DeploymentStepOutcome(step.Name, result.State, result.Message));

                if (result.State == DeploymentStepState.Failed)
                {
                    _logger.LogDebug("Deployment step failed. StepName={StepName}", step.Name);
                    DeploymentFailure failure = result.Failure ?? new DeploymentFailure(
                        DeploymentOperationNames.ForStep(step.Name),
                        DeploymentFailureKinds.Validation,
                        DeploymentFailureReasons.InvalidState);
                    throw new DeploymentOperationException(failure, result.Message);
                }

                if (result.State == DeploymentStepState.Succeeded)
                {
                    runtimeState.CompletedSteps.Add(step.Name);
                }

                // Record completed mutation before honoring a pending request; this state is needed for diagnostics.
                await executionContext.TrySaveRuntimeStateAsync(CancellationToken.None).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                plan = DeploymentPlan.Build(context, runtimeState, executionContext.Preflight?.UsesTargetStorage,
                    executionContext.NetworkProfileRoamingResolved, executionContext.NetworkProfileRoamingPayload?.DataFiles.Count > 0);
            }

            // Close the UI acceptance window before committing one terminal outcome. A request
            // accepted before the synchronous acknowledgement still wins this final check.
            BeginCompletion();
            cancellationToken.ThrowIfCancellationRequested();
            LogTerminalOutcome(
                LogLevel.Information,
                exception: null,
                operationId,
                outcome: "succeeded",
                context,
                runtimeState,
                failedStepName: null,
                failure: null,
                stopwatch.Elapsed,
                cancelled: false);
            await TrackDeploymentCompletedAsync(
                operationId,
                context,
                runtimeState,
                success: true,
                cancelled: false,
                failedStepName: null,
                failure: null,
                stopwatch.Elapsed,
                CancellationToken.None).ConfigureAwait(false);

            succeeded = true;
            terminalStatus = "Deployment orchestration completed.";
            return new DeploymentResult
            {
                IsSuccess = true,
                Message = "Deployment orchestration completed.",
                LogsDirectoryPath = ResolveLogsDirectory(executionContext)
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested && !isCancellationDeferred)
        {
            BeginCompletion();
            terminalStatus = "Deployment cancelled.";
            string failedStepName = ResolveFailedStepName(runtimeState);
            DeploymentFailure cancellationFailure = new(
                runtimeState.CurrentOperation,
                DeploymentFailureKinds.Cancelled,
                DeploymentFailureReasons.CallerCancelled);
            ApplyTerminalFailure(runtimeState, failedStepName, cancellationFailure);
            if (executionContext is not null)
            {
                RecordInterruptedStep(runtimeState, executionContext, DeploymentStepState.Cancelled, terminalStatus);
                await executionContext.TrySaveRuntimeStateAsync(CancellationToken.None).ConfigureAwait(false);
                await TryRebindLogsToFinalTargetAsync(executionContext, CancellationToken.None).ConfigureAwait(false);
                await executionContext.TrySaveRuntimeStateAsync(CancellationToken.None).ConfigureAwait(false);
            }
            LogTerminalOutcome(
                LogLevel.Warning,
                exception: null,
                operationId,
                outcome: "cancelled",
                context,
                runtimeState,
                failedStepName,
                failure: null,
                stopwatch.Elapsed,
                cancelled: true);
            await TrackDeploymentCompletedAsync(
                operationId,
                context,
                runtimeState,
                success: false,
                cancelled: true,
                failedStepName,
                failure: null,
                stopwatch.Elapsed,
                CancellationToken.None).ConfigureAwait(false);

            return new DeploymentResult
            {
                IsSuccess = false,
                IsCancelled = true,
                Message = "Deployment cancelled.",
                LogsDirectoryPath = ResolveLogsDirectory(executionContext, runtimeState)
            };
        }
        catch (Exception ex)
        {
            BeginCompletion();
            DeploymentFailure failure = DeploymentFailureClassifier.Classify(
                ex,
                runtimeState.CurrentOperation);
            string failedStepName = ResolveFailedStepName(runtimeState);
            ApplyTerminalFailure(runtimeState, failedStepName, failure);
            if (executionContext is not null)
            {
                RecordInterruptedStep(runtimeState, executionContext, DeploymentStepState.Failed,
                    failure.Kind == DeploymentFailureKinds.Timeout ? "Transfer timed out." : HttpConnectionFailure.GetMessage(ex));
                await executionContext.TrySaveRuntimeStateAsync(CancellationToken.None).ConfigureAwait(false);
                await TryRebindLogsToFinalTargetAsync(executionContext, CancellationToken.None).ConfigureAwait(false);
                await executionContext.TrySaveRuntimeStateAsync(CancellationToken.None).ConfigureAwait(false);
            }
            LogTerminalOutcome(
                LogLevel.Error,
                ex,
                operationId,
                outcome: "failed",
                context,
                runtimeState,
                failedStepName,
                failure,
                stopwatch.Elapsed,
                cancelled: false);
            await TrackDeploymentCompletedAsync(
                operationId,
                context,
                runtimeState,
                success: false,
                cancelled: false,
                failedStepName,
                failure,
                stopwatch.Elapsed,
                CancellationToken.None).ConfigureAwait(false);

            return new DeploymentResult
            {
                IsSuccess = false,
                Message = failure.Kind == DeploymentFailureKinds.Timeout ? "Transfer timed out." : HttpConnectionFailure.GetMessage(ex),
                LogsDirectoryPath = ResolveLogsDirectory(executionContext, runtimeState)
            };
        }
        finally
        {
            executionContext?.Dispose();
            if (succeeded)
            {
                _operationProgressService.Complete(terminalStatus);
            }
            else
            {
                _operationProgressService.Fail(terminalStatus);
            }
        }
    }

    private async Task<DeploymentStepResult> ExecuteSetupAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        // These operations share one user-facing setup responsibility; retain their operation IDs for diagnostics.
        foreach (string name in new[] { DeploymentStepNames.ValidateTargetConfiguration, DeploymentStepNames.ResolveCacheStrategy, DeploymentStepNames.PreflightDeployment })
        {
            context.SetCurrentOperation(DeploymentOperationNames.ForStep(name));
            DeploymentStepResult result = await _steps[name].ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
            if (result.State == DeploymentStepState.Failed) return result;
        }
        if (_networkProfileRoamingArtifactService is not null)
        {
            context.NetworkProfileRoamingPayload = await _networkProfileRoamingArtifactService.LoadAsync(
                context.Request.Network.ProfileRoaming, context.RuntimeState.WorkspaceRoot, cancellationToken).ConfigureAwait(false);
        }
        context.NetworkProfileRoamingResolved = true;
        return DeploymentStepResult.Succeeded("Target configuration validated.");
    }

    private static void RecordInterruptedStep(DeploymentRuntimeState state, DeploymentStepExecutionContext context,
        DeploymentStepState outcome, string message)
    {
        // A cancellation accepted after a completed mutation must not rewrite that mutation's successful result.
        if (string.IsNullOrWhiteSpace(state.CurrentStep) || state.StepOutcomes.Any(item => item.Name == state.CurrentStep)) return;
        state.StepOutcomes.Add(new DeploymentStepOutcome(state.CurrentStep, outcome, message));
        context.EmitCurrentStep(outcome, message, stepSubProgressIndeterminate: false, stepSubProgressLabel: message);
    }

    // New live stages defer cancellation by default so a process kill cannot interrupt target mutation or servicing cleanup.
    private static bool CanCancelActiveStep(string stepName) => stepName is
        DeploymentStepNames.ValidateCustomUnattend or
        DeploymentStepNames.ValidateTargetConfiguration or
        DeploymentStepNames.CheckWindowsImage or
        DeploymentStepNames.DownloadOperatingSystemImage or
        DeploymentStepNames.DownloadDriverPack or
        DeploymentStepNames.DownloadFirmwareUpdate;

    private Task TrackDeploymentCompletedAsync(
        string operationId,
        DeploymentContext context,
        DeploymentRuntimeState? runtimeState,
        bool success,
        bool cancelled,
        string? failedStepName,
        DeploymentFailure? failure,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        HardwareProfile? hardware = runtimeState?.HardwareProfile;
        OperatingSystemCatalogItem? catalogImage = context.OperatingSystem as OperatingSystemCatalogItem;
        DeploymentRebootTelemetryValue rebootPolicy = DeploymentRebootTelemetryValueResolver.Resolve(
            context.Completion.AutomaticRebootEnabled,
            context.Completion.AutomaticRebootDelaySeconds);
        var properties = new Dictionary<string, object?>
        {
            ["operation_id"] = operationId,
            ["deploy_session_success"] = success,
            ["deploy_session_cancelled"] = cancelled,
            ["deploy_session_duration_seconds"] = Math.Round(duration.TotalSeconds, 2),
            ["deploy_session_completed_step_count"] = runtimeState?.CompletedSteps.Count ?? 0,
            ["deploy_session_failed_step_name"] = failedStepName,
            ["deploy_session_mode"] = context.Mode.ToString().ToLowerInvariant(),
            ["deploy_session_dry_run_enabled"] = context.IsDryRun,
            ["deploy_hardware_vendor"] = NormalizeTelemetryString(hardware?.Manufacturer),
            ["deploy_hardware_model"] = NormalizeTelemetryString(hardware?.Model),
            ["deploy_hardware_virtual_machine"] = hardware?.IsVirtualMachine ?? false,
            ["deploy_os_product"] = ResolveOperatingSystemProduct(context.OperatingSystem),
            ["deploy_os_version"] = NormalizeTelemetryString(catalogImage?.ReleaseId),
            ["deploy_os_build"] = NormalizeTelemetryString(catalogImage?.Build),
            ["deploy_os_update_month"] = context.OperatingSystem.MediaDate == default
                ? "unknown"
                : context.OperatingSystem.MediaDate.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            ["deploy_os_architecture"] = NormalizeTelemetryString(catalogImage?.Architecture),
            ["deploy_os_language"] = NormalizeTelemetryString(catalogImage?.LanguageCode),
            ["deploy_os_edition"] = NormalizeTelemetryString(catalogImage?.Edition),
            ["deploy_os_license_channel"] = NormalizeTelemetryString(catalogImage?.LicenseChannel),
            ["deploy_os_image_index"] = runtimeState?.AppliedImageIndex,
            ["deploy_driver_pack_selection_kind"] = context.DriverPackSelectionKind.ToString().ToLowerInvariant(),
            ["deploy_driver_pack_vendor"] = NormalizeTelemetryString(context.DriverPack?.Manufacturer, "none"),
            ["deploy_driver_pack_model"] = ResolveDriverPackCatalogModel(context.DriverPack),
            ["deploy_firmware_updates_enabled"] = context.ApplyFirmwareUpdates,
            ["deploy_autopilot_enabled"] = context.IsAutopilotEnabled,
            ["deploy_autopilot_provisioning_mode"] = NormalizeTelemetryString(ResolveAutopilotProvisioningMode(context)),
            ["deploy_autopilot_hash_upload_state"] = NormalizeTelemetryString(runtimeState?.AutopilotHardwareHashUploadState.ToString()),
            ["deploy_autopilot_hash_group_tag_selected"] = !string.IsNullOrWhiteSpace(runtimeState?.AutopilotHardwareHashGroupTag),
            ["deploy_unattend_mode"] = context.UsesCustomUnattend ? "custom" : "native",
            ["deploy_oobe_enabled"] = !context.UsesCustomUnattend && context.Oobe.IsEnabled,
            ["deploy_oobe_administrator_enabled"] = !context.UsesCustomUnattend && context.Oobe.EnableAdministratorAccount,
            ["deploy_oobe_additional_account_count"] = context.UsesCustomUnattend ? 0 : context.Oobe.AdditionalAccounts.Count,
            ["deploy_oobe_account_creation_skipped"] = !context.UsesCustomUnattend && context.Oobe.AdditionalAccounts.Count > 0,
            ["deploy_completion_reboot_mode"] = rebootPolicy.Mode
        };

        if (rebootPolicy.DelaySeconds is not null)
        {
            properties["deploy_completion_reboot_delay_seconds"] = rebootPolicy.DelaySeconds.Value;
        }

        if (failure is not null)
        {
            properties["deploy_session_failed_operation_name"] = failure.OperationName;
            properties["deploy_session_failure_kind"] = failure.Kind;
            properties["deploy_session_failure_reason"] = failure.Reason;
            properties["deploy_session_failure_code"] = failure.Code;
        }

        _logger.LogDebug(
            "Tracking deployment telemetry event. Success={Success}, Cancelled={Cancelled}, DurationSeconds={DurationSeconds}, CompletedStepCount={CompletedStepCount}, FailedStepName={FailedStepName}, Mode={Mode}, IsDryRun={IsDryRun}, HardwareVendor={HardwareVendor}, HardwareModel={HardwareModel}, OsProduct={OsProduct}, OsVersion={OsVersion}, DriverPackSelectionKind={DriverPackSelectionKind}, DriverPackVendor={DriverPackVendor}, DriverPackModel={DriverPackModel}.",
            success,
            cancelled,
            properties["deploy_session_duration_seconds"],
            properties["deploy_session_completed_step_count"],
            failedStepName,
            properties["deploy_session_mode"],
            context.IsDryRun,
            properties["deploy_hardware_vendor"],
            properties["deploy_hardware_model"],
            properties["deploy_os_product"],
            properties["deploy_os_version"],
            properties["deploy_driver_pack_selection_kind"],
            properties["deploy_driver_pack_vendor"],
            properties["deploy_driver_pack_model"]);

        return _telemetryService.TrackAsync(TelemetryEvents.DeploySessionFinished, properties, cancellationToken);
    }

    private void LogTerminalOutcome(
        LogLevel level,
        Exception? exception,
        string operationId,
        string outcome,
        DeploymentContext context,
        DeploymentRuntimeState? runtimeState,
        string? failedStepName,
        DeploymentFailure? failure,
        TimeSpan duration,
        bool cancelled)
    {
        _logger.Log(
            level,
            eventId: default,
            exception,
            "Deployment operation finished. OperationId={OperationId}, Outcome={Outcome}, DurationMs={DurationMs}, CompletedStepCount={CompletedStepCount}, FailedStepName={FailedStepName}, FailedOperationName={FailedOperationName}, FailureKind={FailureKind}, FailureReason={FailureReason}, FailureCode={FailureCode}, Mode={Mode}, IsDryRun={IsDryRun}, Cancelled={Cancelled}",
            operationId,
            outcome,
            Math.Round(duration.TotalMilliseconds, 0),
            runtimeState?.CompletedSteps.Count ?? 0,
            failedStepName,
            failure?.OperationName,
            failure?.Kind,
            failure?.Reason,
            failure?.Code,
            context.Mode,
            context.IsDryRun,
            cancelled);
    }

    private static string ResolveFailedStepName(DeploymentRuntimeState runtimeState)
    {
        return string.IsNullOrWhiteSpace(runtimeState.CurrentStep)
            ? "unknown"
            : runtimeState.CurrentStep;
    }

    private static string ResolveOperatingSystemProduct(OperatingSystemMetadata operatingSystem)
    {
        return string.IsNullOrWhiteSpace(operatingSystem.WindowsRelease)
            ? "windows"
            : $"windows_{NormalizeTelemetryString(operatingSystem.WindowsRelease)}";
    }

    private static string ResolveAutopilotProvisioningMode(DeploymentContext context)
    {
        if (!context.IsAutopilotEnabled)
        {
            return "disabled";
        }

        return context.AutopilotProvisioningMode switch
        {
            AutopilotProvisioningMode.HardwareHashUpload => "hardware_hash_upload",
            AutopilotProvisioningMode.InteractiveHardwareHashUpload => "interactive_hardware_hash_upload",
            _ => "json_profile"
        };
    }

    private static string? NormalizeOptionalString(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
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

    private static async Task TryRebindLogsToFinalTargetAsync(
        DeploymentStepExecutionContext executionContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(executionContext.RuntimeState.TargetWindowsPartitionRoot))
        {
            return;
        }

        string finalRoot = DeploymentStorageLayout.FromPartitionRoot(executionContext.RuntimeState.TargetWindowsPartitionRoot).Root;
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
            string logs = DeploymentStorageLayout.FromPartitionRoot(effectiveRuntimeState.TargetWindowsPartitionRoot).LogsDeployment;
            if (Directory.Exists(logs)) return logs;
        }

        return executionContext?.ResolveWorkspaceLogsPath() ?? string.Empty;
    }

    private static void ApplyTerminalFailure(
        DeploymentRuntimeState runtimeState,
        string failedStepName,
        DeploymentFailure failure)
    {
        runtimeState.LastFailureStep = failedStepName;
        runtimeState.LastFailureKind = failure.Kind;
        runtimeState.LastFailureReason = failure.Reason;
        runtimeState.LastFailureCode = failure.Code;
    }
}
