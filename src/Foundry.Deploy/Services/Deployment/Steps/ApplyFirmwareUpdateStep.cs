using System.IO;
using Foundry.Deploy.Services.Logging;

namespace Foundry.Deploy.Services.Deployment.Steps;

public sealed class ApplyFirmwareUpdateStep : DeploymentStepBase
{
    private readonly IWindowsDeploymentService _windowsDeploymentService;

    public ApplyFirmwareUpdateStep(IWindowsDeploymentService windowsDeploymentService)
    {
        _windowsDeploymentService = windowsDeploymentService;
    }

    public override int Order => 15;

    public override string Name => DeploymentStepNames.ApplyFirmwareUpdate;

    protected override async Task<DeploymentStepResult> ExecuteLiveAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        string firmwareRoot = context.RuntimeState.ExtractedFirmwarePath ?? string.Empty;
        if (!Directory.Exists(firmwareRoot))
        {
            return DeploymentStepResult.Skipped("No extracted firmware payload is available.");
        }

        int infCount = Directory.EnumerateFiles(firmwareRoot, "*.inf", SearchOption.AllDirectories).Count();
        if (infCount == 0)
        {
            return DeploymentStepResult.Skipped("The extracted firmware payload does not contain any INF files.");
        }

        if (string.IsNullOrWhiteSpace(context.RuntimeState.TargetWindowsPartitionRoot))
        {
            return DeploymentStepResult.Failed("Target Windows partition is unavailable.");
        }

        string targetFoundryRoot = context.EnsureTargetFoundryRoot();
        string workingDirectory = Path.Combine(targetFoundryRoot, "Temp", "Deployment");
        string scratchDirectory = Path.Combine(targetFoundryRoot, "Temp", "Dism");
        const string stepMessage = "Applying firmware update...";

        context.EmitCurrentStepIndeterminate(stepMessage, "Injecting firmware payload into offline Windows...");
        IProgress<double> progress = context.CreateStepPercentProgressReporter(stepMessage, "Applying");

        await _windowsDeploymentService
            .ApplyOfflineDriversAsync(
                context.RuntimeState.TargetWindowsPartitionRoot,
                firmwareRoot,
                scratchDirectory,
                workingDirectory,
                cancellationToken,
                progress)
            .ConfigureAwait(false);

        string firmwareLabel = string.IsNullOrWhiteSpace(context.RuntimeState.FirmwareUpdateTitle)
            ? "Firmware update"
            : context.RuntimeState.FirmwareUpdateTitle!;
        await context.AppendLogAsync(
            DeploymentLogLevel.Info,
            $"{firmwareLabel} applied offline to Windows: {infCount} INF files from '{firmwareRoot}'.",
            cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("Firmware update applied.");
    }

    protected override async Task<DeploymentStepResult> ExecuteDryRunAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        string firmwareRoot = context.RuntimeState.ExtractedFirmwarePath ?? string.Empty;
        if (!Directory.Exists(firmwareRoot))
        {
            await Task.Delay(120, cancellationToken).ConfigureAwait(false);
            return DeploymentStepResult.Skipped("No extracted firmware payload is available.");
        }

        int infCount = Directory.EnumerateFiles(firmwareRoot, "*.inf", SearchOption.AllDirectories).Count();
        if (infCount == 0)
        {
            await Task.Delay(120, cancellationToken).ConfigureAwait(false);
            return DeploymentStepResult.Skipped("The extracted firmware payload does not contain any INF files.");
        }

        await context.AppendLogAsync(
            DeploymentLogLevel.Info,
            $"[DRY-RUN] Simulated firmware apply to offline Windows: {infCount} INF files from '{firmwareRoot}'.",
            cancellationToken).ConfigureAwait(false);
        await Task.Delay(150, cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("Firmware update applied (simulation).");
    }
}
