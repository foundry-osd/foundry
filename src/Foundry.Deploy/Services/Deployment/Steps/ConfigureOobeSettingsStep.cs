using System.IO;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Logging;

namespace Foundry.Deploy.Services.Deployment.Steps;

/// <summary>
/// Applies configured offline first-run policies before recovery and provisioning steps run.
/// </summary>
public sealed class ConfigureOobeSettingsStep : DeploymentStepBase
{
    private readonly IWindowsDeploymentService _windowsDeploymentService;

    /// <summary>
    /// Initializes a deployment step that writes first-run defaults to the offline Windows installation.
    /// </summary>
    public ConfigureOobeSettingsStep(IWindowsDeploymentService windowsDeploymentService)
    {
        _windowsDeploymentService = windowsDeploymentService;
    }

    /// <inheritdoc />
    public override int Order => 9;

    /// <inheritdoc />
    public override string Name => DeploymentStepNames.ConfigureOobeSettings;

    /// <inheritdoc />
    protected override async Task<DeploymentStepResult> ExecuteLiveAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        bool shouldConfigureOobe = context.RuntimeState.Oobe.IsEnabled;
        bool shouldConfigureAiPolicies = HasAnyAiPolicyOptionEnabled(context.RuntimeState.AiComponentRemoval);
        if (!shouldConfigureOobe && !shouldConfigureAiPolicies)
        {
            return DeploymentStepResult.Succeeded("Offline customization disabled.");
        }

        if (string.IsNullOrWhiteSpace(context.RuntimeState.TargetWindowsPartitionRoot))
        {
            return DeploymentStepResult.Failed("Target Windows partition is unavailable.");
        }

        string targetFoundryRoot = context.EnsureTargetFoundryRoot();
        string workingDirectory = Path.Combine(targetFoundryRoot, "Temp", "Deployment");
        Directory.CreateDirectory(workingDirectory);

        if (shouldConfigureOobe)
        {
            context.EmitCurrentStepIndeterminate("Configuring OOBE settings...", "Writing first-run privacy defaults...");
            await _windowsDeploymentService
                .ConfigureOfflineOobeAsync(
                    context.RuntimeState.TargetWindowsPartitionRoot,
                    context.RuntimeState.Oobe,
                    context.Request.OperatingSystem.Architecture,
                    workingDirectory,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (shouldConfigureAiPolicies)
        {
            context.EmitCurrentStepIndeterminate("Configuring AI component removal...", "Writing offline AI policies...");
            await _windowsDeploymentService
                .ConfigureOfflineAiComponentRemovalAsync(
                    context.RuntimeState.TargetWindowsPartitionRoot,
                    context.RuntimeState.AiComponentRemoval,
                    workingDirectory,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await context.AppendLogAsync(
            DeploymentLogLevel.Info,
            "Offline customization configured.",
            cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("Offline customization configured.");
    }

    /// <inheritdoc />
    protected override async Task<DeploymentStepResult> ExecuteDryRunAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        bool shouldConfigureOobe = context.RuntimeState.Oobe.IsEnabled;
        bool shouldConfigureAiPolicies = HasAnyAiPolicyOptionEnabled(context.RuntimeState.AiComponentRemoval);
        if (!shouldConfigureOobe && !shouldConfigureAiPolicies)
        {
            return DeploymentStepResult.Succeeded("Offline customization disabled.");
        }

        if (string.IsNullOrWhiteSpace(context.RuntimeState.TargetWindowsPartitionRoot))
        {
            return DeploymentStepResult.Failed("Target Windows partition is unavailable.");
        }

        context.EmitCurrentStepIndeterminate("Configuring offline customizations...", "Writing first-run defaults...");
        await context.AppendLogAsync(
            DeploymentLogLevel.Info,
            "[DRY-RUN] Simulated offline customization.",
            cancellationToken).ConfigureAwait(false);
        await Task.Delay(120, cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("Offline customization configured (simulation).");
    }

    private static bool HasAnyAiPolicyOptionEnabled(DeployAiComponentRemovalSettings settings)
    {
        return settings.IsEnabled &&
            (settings.RemoveCopilot ||
                settings.DisableRecall ||
                settings.DisableClickToDo ||
                settings.DisableAiServiceAutoStart ||
                settings.DisableEdgeAi ||
                settings.DisablePaintAi ||
                settings.DisableNotepadAi);
    }
}
