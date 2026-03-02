using System.IO;
using System.Text.Json;
using Foundry.Deploy.Models;
using Foundry.Deploy.Services.Autopilot;
using Foundry.Deploy.Services.Hardware;
using Foundry.Deploy.Services.Logging;

namespace Foundry.Deploy.Services.Deployment.Steps;

public sealed class ExecuteFullAutopilotWorkflowStep : DeploymentStepBase
{
    private readonly IAutopilotService _autopilotService;
    private readonly IHardwareProfileService _hardwareProfileService;

    public ExecuteFullAutopilotWorkflowStep(
        IAutopilotService autopilotService,
        IHardwareProfileService hardwareProfileService)
    {
        _autopilotService = autopilotService;
        _hardwareProfileService = hardwareProfileService;
    }

    public override int Order => 14;

    public override string Name => DeploymentStepNames.ExecuteFullAutopilotWorkflow;

    protected override async Task<DeploymentStepResult> ExecuteLiveAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        if (!context.Request.UseFullAutopilot)
        {
            return DeploymentStepResult.Skipped("Autopilot is disabled.");
        }

        string targetFoundryRoot = context.EnsureTargetFoundryRoot();
        if (string.IsNullOrWhiteSpace(context.RuntimeState.TargetWindowsPartitionRoot))
        {
            return DeploymentStepResult.Failed("Target Windows partition is unavailable for Autopilot artifacts.");
        }

        HardwareProfile hardware = context.RuntimeState.HardwareProfile
            ?? await _hardwareProfileService.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        AutopilotExecutionResult result = await _autopilotService
            .ExecuteFullWorkflowAsync(
                targetFoundryRoot,
                context.RuntimeState.TargetWindowsPartitionRoot,
                hardware,
                context.Request.OperatingSystem,
                context.Request.AllowAutopilotDeferredCompletion,
                cancellationToken)
            .ConfigureAwait(false);

        context.RuntimeState.AutopilotWorkflowPath = result.WorkflowManifestPath;
        await context.AppendLogAsync(DeploymentLogLevel.Info, $"Autopilot transcript: {result.TranscriptPath}", cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return DeploymentStepResult.Failed(result.Message);
        }

        if (result.DeferredCompletionPrepared)
        {
            await context.AppendLogAsync(DeploymentLogLevel.Warning, result.Message, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(result.DeferredScriptPath))
            {
                await context.AppendLogAsync(DeploymentLogLevel.Warning, $"Deferred script: {result.DeferredScriptPath}", cancellationToken).ConfigureAwait(false);
            }

            if (!string.IsNullOrWhiteSpace(result.SetupCompleteHookPath))
            {
                await context.AppendLogAsync(DeploymentLogLevel.Warning, $"SetupComplete hook: {result.SetupCompleteHookPath}", cancellationToken).ConfigureAwait(false);
            }

            return DeploymentStepResult.Succeeded("Autopilot deferred completion prepared.");
        }

        await context.AppendLogAsync(DeploymentLogLevel.Info, result.Message, cancellationToken).ConfigureAwait(false);
        return DeploymentStepResult.Succeeded("Autopilot workflow completed.");
    }

    protected override async Task<DeploymentStepResult> ExecuteDryRunAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        if (!context.Request.UseFullAutopilot)
        {
            await Task.Delay(120, cancellationToken).ConfigureAwait(false);
            return DeploymentStepResult.Skipped("Autopilot is disabled.");
        }

        string targetFoundryRoot = context.EnsureTargetFoundryRoot();
        string autopilotRoot = Path.Combine(targetFoundryRoot, "Autopilot");
        Directory.CreateDirectory(autopilotRoot);
        string manifestPath = Path.Combine(autopilotRoot, "autopilot-workflow.dryrun.json");

        string manifest = JsonSerializer.Serialize(new
        {
            createdAtUtc = DateTimeOffset.UtcNow,
            mode = "dry-run",
            note = "Debug safe mode simulation. No online registration executed."
        }, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        await File.WriteAllTextAsync(manifestPath, manifest, cancellationToken).ConfigureAwait(false);
        context.RuntimeState.AutopilotWorkflowPath = manifestPath;

        await context.AppendLogAsync(DeploymentLogLevel.Info, $"[DRY-RUN] Autopilot workflow simulated: {manifestPath}", cancellationToken).ConfigureAwait(false);
        await Task.Delay(150, cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("Autopilot workflow completed (simulation).");
    }
}
