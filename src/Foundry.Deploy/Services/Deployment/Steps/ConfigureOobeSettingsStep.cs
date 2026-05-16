using System.IO;
using Foundry.Deploy.Services.Logging;

namespace Foundry.Deploy.Services.Deployment.Steps;

/// <summary>
/// Applies configured Windows OOBE defaults before recovery and provisioning steps run.
/// </summary>
public sealed class ConfigureOobeSettingsStep : DeploymentStepBase
{
    private readonly IWindowsDeploymentService _windowsDeploymentService;

    /// <summary>
    /// Initializes a deployment step that writes OOBE defaults to the offline Windows installation.
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
        if (!context.RuntimeState.Oobe.IsEnabled)
        {
            return DeploymentStepResult.Succeeded("OOBE customization disabled.");
        }

        if (string.IsNullOrWhiteSpace(context.RuntimeState.TargetWindowsPartitionRoot))
        {
            return DeploymentStepResult.Failed("Target Windows partition is unavailable.");
        }

        string targetFoundryRoot = context.EnsureTargetFoundryRoot();
        string workingDirectory = Path.Combine(targetFoundryRoot, "Temp", "Deployment");
        Directory.CreateDirectory(workingDirectory);

        context.EmitCurrentStepIndeterminate("Configuring OOBE settings...", "Writing first-run privacy defaults...");
        await _windowsDeploymentService
            .ConfigureOfflineOobeAsync(
                context.RuntimeState.TargetWindowsPartitionRoot,
                context.RuntimeState.Oobe,
                context.Request.OperatingSystem.Architecture,
                workingDirectory,
                cancellationToken)
            .ConfigureAwait(false);

        await context.AppendLogAsync(
            DeploymentLogLevel.Info,
            "OOBE customization configured.",
            cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("OOBE settings configured.");
    }

    /// <inheritdoc />
    protected override async Task<DeploymentStepResult> ExecuteDryRunAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        if (!context.RuntimeState.Oobe.IsEnabled)
        {
            return DeploymentStepResult.Succeeded("OOBE customization disabled.");
        }

        if (string.IsNullOrWhiteSpace(context.RuntimeState.TargetWindowsPartitionRoot))
        {
            return DeploymentStepResult.Failed("Target Windows partition is unavailable.");
        }

        context.EmitCurrentStepIndeterminate("Configuring OOBE settings...", "Writing first-run privacy defaults...");
        await context.AppendLogAsync(
            DeploymentLogLevel.Info,
            "[DRY-RUN] Simulated OOBE customization.",
            cancellationToken).ConfigureAwait(false);
        await Task.Delay(120, cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("OOBE settings configured (simulation).");
    }
}
