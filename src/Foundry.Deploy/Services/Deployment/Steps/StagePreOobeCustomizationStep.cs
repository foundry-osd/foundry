// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using Foundry.Deploy.Services.Deployment.PreOobe;
using Foundry.Deploy.Services.DriverPacks;
using Foundry.Deploy.Services.Logging;

namespace Foundry.Deploy.Services.Deployment.Steps;

/// <summary>
/// Assembles setup scripts after deferred driver payloads and network artifacts have been prepared.
/// </summary>
public sealed class StagePreOobeCustomizationStep : DeploymentStepBase
{
    private readonly IPreOobeScriptProvisioningService _preOobeScriptProvisioningService;
    private readonly PreOobeScriptDefinitionBuilder _preOobeScriptDefinitionBuilder;
    private readonly IDriverPackStrategyResolver _driverPackStrategyResolver;

    public StagePreOobeCustomizationStep(
        IPreOobeScriptProvisioningService preOobeScriptProvisioningService,
        PreOobeScriptDefinitionBuilder preOobeScriptDefinitionBuilder,
        IDriverPackStrategyResolver driverPackStrategyResolver)
    {
        _preOobeScriptProvisioningService = preOobeScriptProvisioningService;
        _preOobeScriptDefinitionBuilder = preOobeScriptDefinitionBuilder;
        _driverPackStrategyResolver = driverPackStrategyResolver;
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

        IReadOnlyList<PreOobeScriptDefinition> scripts = _preOobeScriptDefinitionBuilder.Build(
            context.RuntimeState.AppxRemoval,
            context.RuntimeState.AiComponentRemoval,
            driverPackSettings,
            context.NetworkProfileRoamingPayload,
            activateWindowsOem: ShouldActivateWindowsOem(context.Request));
        if (scripts.Count == 0)
        {
            return DeploymentStepResult.Skipped("No pre-OOBE customization scripts are required.");
        }

        context.EmitCurrentStepIndeterminate("Staging pre-OOBE customizations...", "Updating SetupComplete hook...", DeploymentOperationNames.StagePreOobe);
        PreOobeScriptProvisioningResult result = _preOobeScriptProvisioningService.Provision(
            context.RuntimeState.TargetWindowsPartitionRoot,
            scripts,
            context.RuntimeState.OperationId);

        ApplyPreOobeResult(context.RuntimeState, result);

        await context.AppendLogAsync(
            DeploymentLogLevel.Info,
            $"Pre-OOBE customization staged with {scripts.Count} script(s). SetupComplete hook: '{result.SetupCompletePath}'.",
            cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("Pre-OOBE customizations staged.");
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

        IReadOnlyList<PreOobeScriptDefinition> scripts = _preOobeScriptDefinitionBuilder.Build(
            context.RuntimeState.AppxRemoval,
            context.RuntimeState.AiComponentRemoval,
            driverPackSettings,
            context.NetworkProfileRoamingPayload,
            activateWindowsOem: ShouldActivateWindowsOem(context.Request));
        if (scripts.Count == 0)
        {
            await Task.Delay(80, cancellationToken).ConfigureAwait(false);
            return DeploymentStepResult.Skipped("No pre-OOBE customization scripts are required.");
        }

        ApplyDryRunPreOobeResult(context.RuntimeState, scripts);

        await context.AppendLogAsync(
            DeploymentLogLevel.Info,
            $"[DRY-RUN] Simulated pre-OOBE customization staging with {scripts.Count} script(s).",
            cancellationToken).ConfigureAwait(false);
        await Task.Delay(120, cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("Pre-OOBE customizations staged (simulation).");
    }

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

    private static void ApplyPreOobeResult(
        DeploymentRuntimeState runtimeState,
        PreOobeScriptProvisioningResult result)
    {
        runtimeState.PreOobeSetupCompletePath = result.SetupCompletePath;
        runtimeState.PreOobeRunnerPath = result.RunnerPath;
        runtimeState.PreOobeManifestPath = result.ManifestPath;
        runtimeState.PreOobeScriptPaths = result.StagedScriptPaths;
    }

    private static void ApplyDryRunPreOobeResult(
        DeploymentRuntimeState runtimeState,
        IReadOnlyList<PreOobeScriptDefinition> scripts)
    {
        DeploymentStorageLayout layout = DeploymentStorageLayout.FromPartitionRoot(runtimeState.TargetWindowsPartitionRoot!);
        string preOobeRoot = layout.RuntimePreOobe;

        runtimeState.PreOobeSetupCompletePath = Path.Combine(
            runtimeState.TargetWindowsPartitionRoot!,
            "Windows",
            "Setup",
            "Scripts",
            "SetupComplete.cmd");
        runtimeState.PreOobeRunnerPath = Path.Combine(preOobeRoot, "Invoke-FoundryPreOobe.ps1");
        runtimeState.PreOobeManifestPath = Path.Combine(layout.StatePreOobe, "pre-oobe-manifest.json");
        runtimeState.PreOobeScriptPaths = scripts
            .Select(script => Path.Combine(preOobeRoot, "Scripts", script.FileName))
            .ToArray();
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
