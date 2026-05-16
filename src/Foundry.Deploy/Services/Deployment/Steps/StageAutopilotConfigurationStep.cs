using System.IO;
using System.Text.Json;
using Foundry.Deploy.Services.Logging;

namespace Foundry.Deploy.Services.Deployment.Steps;

public sealed class StageAutopilotConfigurationStep : DeploymentStepBase
{
    private const string TargetConfigurationRelativePath = @"Windows\Provisioning\Autopilot\AutopilotConfigurationFile.json";

    public override int Order => 17;

    public override string Name => DeploymentStepNames.StageAutopilotConfiguration;

    protected override async Task<DeploymentStepResult> ExecuteLiveAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        if (!context.Request.IsAutopilotEnabled)
        {
            return DeploymentStepResult.Skipped("Autopilot is disabled.");
        }

        if (context.Request.SelectedAutopilotProfile is null)
        {
            return DeploymentStepResult.Failed("Autopilot is enabled but no profile was selected.");
        }

        if (string.IsNullOrWhiteSpace(context.RuntimeState.TargetWindowsPartitionRoot))
        {
            return DeploymentStepResult.Failed("Target Windows partition is unavailable for Autopilot staging.");
        }

        string sourceConfigurationPath = context.Request.SelectedAutopilotProfile.ConfigurationFilePath;
        if (!File.Exists(sourceConfigurationPath))
        {
            return DeploymentStepResult.Failed(
                $"Selected Autopilot profile file was not found: '{sourceConfigurationPath}'.");
        }

        string targetConfigurationPath = Path.Combine(
            context.RuntimeState.TargetWindowsPartitionRoot,
            TargetConfigurationRelativePath);
        string? targetDirectoryPath = Path.GetDirectoryName(targetConfigurationPath);
        if (string.IsNullOrWhiteSpace(targetDirectoryPath))
        {
            return DeploymentStepResult.Failed("Failed to resolve the target Autopilot directory.");
        }

        context.EmitCurrentStepIndeterminate("Staging Autopilot profile...", "Copying AutopilotConfigurationFile.json...");
        Directory.CreateDirectory(targetDirectoryPath);
        File.Copy(sourceConfigurationPath, targetConfigurationPath, overwrite: true);

        context.RuntimeState.StagedAutopilotConfigurationPath = targetConfigurationPath;
        await context.AppendLogAsync(
            DeploymentLogLevel.Info,
            $"Autopilot profile '{context.Request.SelectedAutopilotProfile.DisplayName}' staged to '{targetConfigurationPath}'.",
            cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("Autopilot profile staged.");
    }

    protected override async Task<DeploymentStepResult> ExecuteDryRunAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        if (!context.Request.IsAutopilotEnabled)
        {
            await Task.Delay(80, cancellationToken).ConfigureAwait(false);
            return DeploymentStepResult.Skipped("Autopilot is disabled.");
        }

        if (context.Request.SelectedAutopilotProfile is null)
        {
            return DeploymentStepResult.Failed("Autopilot is enabled but no profile was selected.");
        }

        string targetFoundryRoot = context.EnsureTargetFoundryRoot();
        string autopilotRoot = Path.Combine(targetFoundryRoot, "Autopilot");
        Directory.CreateDirectory(autopilotRoot);

        string manifestPath = Path.Combine(autopilotRoot, "autopilot-profile-stage.dryrun.json");
        string manifest = JsonSerializer.Serialize(new
        {
            createdAtUtc = DateTimeOffset.UtcNow,
            mode = "dry-run",
            selectedProfileFolderName = context.Request.SelectedAutopilotProfile.FolderName,
            selectedProfileDisplayName = context.Request.SelectedAutopilotProfile.DisplayName,
            sourceConfigurationFilePath = context.Request.SelectedAutopilotProfile.ConfigurationFilePath,
            targetConfigurationRelativePath = TargetConfigurationRelativePath
        }, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        context.EmitCurrentStepIndeterminate("Staging Autopilot profile...", "Writing dry-run Autopilot manifest...");
        await File.WriteAllTextAsync(manifestPath, manifest, cancellationToken).ConfigureAwait(false);
        context.RuntimeState.StagedAutopilotConfigurationPath = manifestPath;

        await context.AppendLogAsync(
            DeploymentLogLevel.Info,
            $"[DRY-RUN] Autopilot profile staging simulated: {manifestPath}",
            cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("Autopilot profile staged (simulation).");
    }
}
