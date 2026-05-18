using System.IO;
using Foundry.Deploy.Services.Deployment.PreOobe;
using Foundry.Deploy.Services.DriverPacks;
using Foundry.Deploy.Services.Logging;

namespace Foundry.Deploy.Services.Deployment.Steps;

/// <summary>
/// Stages pre-OOBE customization scripts that do not require a deferred driver package.
/// </summary>
public sealed class StagePreOobeCustomizationStep : DeploymentStepBase
{
    private readonly IPreOobeScriptProvisioningService _preOobeScriptProvisioningService;
    private readonly PreOobeScriptDefinitionBuilder _preOobeScriptDefinitionBuilder;

    public StagePreOobeCustomizationStep(
        IPreOobeScriptProvisioningService preOobeScriptProvisioningService,
        PreOobeScriptDefinitionBuilder preOobeScriptDefinitionBuilder)
    {
        _preOobeScriptProvisioningService = preOobeScriptProvisioningService;
        _preOobeScriptDefinitionBuilder = preOobeScriptDefinitionBuilder;
    }

    public override int Order => 13;

    public override string Name => DeploymentStepNames.StagePreOobeCustomization;

    protected override async Task<DeploymentStepResult> ExecuteLiveAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        IReadOnlyList<PreOobeScriptDefinition> scripts = _preOobeScriptDefinitionBuilder.Build(context.RuntimeState.AppxRemoval);
        if (scripts.Count == 0)
        {
            return DeploymentStepResult.Skipped("No pre-OOBE customization scripts are required.");
        }

        if (context.RuntimeState.DriverPackInstallMode == DriverPackInstallMode.DeferredSetupComplete)
        {
            return DeploymentStepResult.Skipped("Pre-OOBE customizations will be staged with the deferred driver pack.");
        }

        if (string.IsNullOrWhiteSpace(context.RuntimeState.TargetWindowsPartitionRoot))
        {
            return DeploymentStepResult.Failed("Target Windows partition is unavailable.");
        }

        context.EmitCurrentStepIndeterminate("Staging pre-OOBE customizations...", "Updating SetupComplete hook...");
        PreOobeScriptProvisioningResult result = _preOobeScriptProvisioningService.Provision(
            context.RuntimeState.TargetWindowsPartitionRoot,
            scripts);

        ApplyPreOobeResult(context.RuntimeState, result);

        await context.AppendLogAsync(
            DeploymentLogLevel.Info,
            $"Pre-OOBE AppX removal staged for {context.RuntimeState.AppxRemoval.PackageNames.Count} package(s). SetupComplete hook: '{result.SetupCompletePath}'.",
            cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("Pre-OOBE customizations staged.");
    }

    protected override async Task<DeploymentStepResult> ExecuteDryRunAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        IReadOnlyList<PreOobeScriptDefinition> scripts = _preOobeScriptDefinitionBuilder.Build(context.RuntimeState.AppxRemoval);
        if (scripts.Count == 0)
        {
            await Task.Delay(80, cancellationToken).ConfigureAwait(false);
            return DeploymentStepResult.Skipped("No pre-OOBE customization scripts are required.");
        }

        if (context.RuntimeState.DriverPackInstallMode == DriverPackInstallMode.DeferredSetupComplete)
        {
            await Task.Delay(80, cancellationToken).ConfigureAwait(false);
            return DeploymentStepResult.Skipped("Pre-OOBE customizations will be staged with the deferred driver pack.");
        }

        if (string.IsNullOrWhiteSpace(context.RuntimeState.TargetWindowsPartitionRoot))
        {
            return DeploymentStepResult.Failed("Target Windows partition is unavailable.");
        }

        ApplyDryRunPreOobeResult(context.RuntimeState, scripts);

        await context.AppendLogAsync(
            DeploymentLogLevel.Info,
            $"[DRY-RUN] Simulated pre-OOBE AppX removal staging for {context.RuntimeState.AppxRemoval.PackageNames.Count} package(s).",
            cancellationToken).ConfigureAwait(false);
        await Task.Delay(120, cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("Pre-OOBE customizations staged (simulation).");
    }

    private static void ApplyPreOobeResult(DeploymentRuntimeState runtimeState, PreOobeScriptProvisioningResult result)
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
        string preOobeRoot = Path.Combine(
            runtimeState.TargetWindowsPartitionRoot!,
            "Windows",
            "Temp",
            "Foundry",
            "PreOobe");

        runtimeState.PreOobeSetupCompletePath = Path.Combine(
            runtimeState.TargetWindowsPartitionRoot!,
            "Windows",
            "Setup",
            "Scripts",
            "SetupComplete.cmd");
        runtimeState.PreOobeRunnerPath = Path.Combine(preOobeRoot, "Invoke-FoundryPreOobe.ps1");
        runtimeState.PreOobeManifestPath = Path.Combine(preOobeRoot, "pre-oobe-manifest.json");
        runtimeState.PreOobeScriptPaths = scripts
            .Select(script => Path.Combine(preOobeRoot, "Scripts", script.FileName))
            .ToArray();
    }
}
