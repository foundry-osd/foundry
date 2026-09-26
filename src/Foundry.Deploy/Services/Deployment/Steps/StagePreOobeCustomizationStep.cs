// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using Foundry.Deploy.Services.Deployment.PreOobe;
using Foundry.Deploy.Services.DriverPacks;
using Foundry.Deploy.Services.Logging;
using Foundry.Deploy.Services.Localization;

namespace Foundry.Deploy.Services.Deployment.Steps;

/// <summary>
/// Assembles setup scripts after deferred driver payloads and network artifacts have been prepared.
/// </summary>
public sealed class StagePreOobeCustomizationStep : DeploymentStepBase
{
    private readonly IDriverPackStrategyResolver _driverPackStrategyResolver;
    private readonly PreOobeTargetStagingService _stagingService;

    public StagePreOobeCustomizationStep(IDriverPackStrategyResolver driverPackStrategyResolver, PreOobeTargetStagingService? stagingService = null)
    {
        _driverPackStrategyResolver = driverPackStrategyResolver;
        _stagingService = stagingService ?? new PreOobeTargetStagingService();
    }

    public override string Name => DeploymentStepNames.StagePreOobeCustomization;

    protected override async Task<DeploymentStepResult> ExecuteLiveAsync(
        DeploymentStepExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.RuntimeState.TargetWindowsPartitionRoot))
        {
            return CreateMissingTargetPartitionFailure();
        }

        PreOobeDriverPackScriptSettings? driverPackSettings = null;
        if (context.RuntimeState.DriverPackInstallMode == DriverPackInstallMode.DeferredSetupComplete)
        {
            (driverPackSettings, DeploymentStepResult? failure) = ResolveStagedDriverPackage(context);
            if (failure is not null)
            {
                return failure;
            }
        }

        if (!HasTasks(context))
            return DeploymentStepResult.Skipped(LocalizationText.GetString("PostInstall.NoTasks"));
        context.EmitCurrentStepIndeterminate(LocalizationText.GetString("PostInstall.Staging"), LocalizationText.GetString("PostInstall.Staging"), DeploymentOperationNames.StagePreOobe);
        try
        {
            await _stagingService.StageAsync(context, driverPackSettings, cancellationToken).ConfigureAwait(false);
            return DeploymentStepResult.Succeeded(LocalizationText.GetString("PostInstall.Staged"));
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or InvalidOperationException or global::System.Xml.XmlException)
        {
            return DeploymentStepResult.Failed(LocalizationText.GetString("PostInstall.StagingFailed"),
                DeploymentFailure.Guard(DeploymentOperationNames.StagePreOobe, DeploymentFailureReasons.InvalidInput, "postinstall_staging_failed"));
        }
    }
    protected override async Task<DeploymentStepResult> ExecuteDryRunAsync(
        DeploymentStepExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.RuntimeState.TargetWindowsPartitionRoot))
        {
            return CreateMissingTargetPartitionFailure();
        }

        PreOobeDriverPackScriptSettings? driverPackSettings = null;
        if (context.RuntimeState.DriverPackInstallMode == DriverPackInstallMode.DeferredSetupComplete)
        {
            (driverPackSettings, DeploymentStepResult? failure) = ResolveStagedDriverPackage(context);
            if (failure is not null)
            {
                return failure;
            }
        }

        if (!HasTasks(context)) return DeploymentStepResult.Skipped(LocalizationText.GetString("PostInstall.NoTasks"));
        ApplyDryRunPreOobeResult(context.RuntimeState);
        await context.AppendLogAsync(DeploymentLogLevel.Info, "[DRY-RUN] Simulated native post-installation staging.", cancellationToken).ConfigureAwait(false);
        return DeploymentStepResult.Succeeded(LocalizationText.GetString("PostInstall.Simulated"));
    }

    private static bool HasTasks(DeploymentStepExecutionContext context) => PreOobeContentResolver.IsRequired(context.Request) ||
        context.RuntimeState.DriverPackInstallMode == DriverPackInstallMode.DeferredSetupComplete || context.NetworkProfileRoamingPayload?.DataFiles.Count > 0;

    private (PreOobeDriverPackScriptSettings? Settings, DeploymentStepResult? Failure) ResolveStagedDriverPackage(
        DeploymentStepExecutionContext context)
    {
        string stagedPath = context.RuntimeState.DeferredDriverPackagePath ?? string.Empty;
        if (string.IsNullOrWhiteSpace(stagedPath) || (!context.Request.IsDryRun && !File.Exists(stagedPath)))
        {
            return (null, DeploymentStepResult.Failed(
                "Driver pack source payload is unavailable for deferred staging.",
                DeploymentFailure.Guard(DeploymentOperationNames.StageDeferredDriverPack,
                    DeploymentFailureReasons.MissingResource, "missing_driver_payload")));
        }

        DriverPackExecutionPlan plan = _driverPackStrategyResolver.Resolve(
            context.Request.DriverPackSelectionKind, context.Request.DriverPack, stagedPath);
        if (plan.DeferredCommandKind == DeferredDriverPackageCommandKind.None)
        {
            return (null, DeploymentStepResult.Failed(
                "Deferred driver pack staging was requested without a supported deferred command.",
                DeploymentFailure.Guard(DeploymentOperationNames.StageDeferredDriverPack,
                    DeploymentFailureReasons.InvalidInput, "unsupported_deferred_driver_command")));
        }

        return (new PreOobeDriverPackScriptSettings
        {
            CommandKind = plan.DeferredCommandKind,
            RuntimePackagePath = DeploymentStorageLayout.RuntimePath(Path.Combine("Payloads", "Drivers", Path.GetFileName(stagedPath)))
        }, null);
    }

    private static void ApplyDryRunPreOobeResult(DeploymentRuntimeState runtimeState)
    {
        DeploymentStorageLayout layout = DeploymentStorageLayout.FromPartitionRoot(runtimeState.TargetWindowsPartitionRoot!);
        runtimeState.PreOobeSetupCompletePath = null;
        runtimeState.PreOobeRunnerPath = Path.Combine(layout.RuntimePreOobe, "Foundry.PostInstall.exe");
        runtimeState.PreOobeManifestPath = Path.Combine(layout.StatePreOobe, "plan.json");
        runtimeState.PreOobeScriptPaths = [];
    }

    /// <summary>Preserves automatic OEM activation only for native retail-image deployments.</summary>
    public static bool ShouldActivateWindowsOem(DeploymentContext request) =>
        !request.UsesCustomUnattend &&
        string.Equals(request.OperatingSystem.LicenseChannel, "RET", StringComparison.OrdinalIgnoreCase);

    private static DeploymentStepResult CreateMissingTargetPartitionFailure() =>
        DeploymentStepResult.Failed(
            "Target Windows partition is unavailable.",
            DeploymentFailure.Guard(
                DeploymentOperationNames.StagePreOobe,
                DeploymentFailureReasons.MissingResource,
                "missing_target_partition"));
}
