using System.IO;
using Foundry.Deploy.Services.Logging;

namespace Foundry.Deploy.Services.Deployment.Steps;

public sealed class ApplyOfflineDriversStep : DeploymentStepBase
{
    private readonly IWindowsDeploymentService _windowsDeploymentService;

    public ApplyOfflineDriversStep(IWindowsDeploymentService windowsDeploymentService)
    {
        _windowsDeploymentService = windowsDeploymentService;
    }

    public override int Order => 11;

    public override string Name => DeploymentStepNames.ApplyOfflineDrivers;

    protected override async Task<DeploymentStepResult> ExecuteLiveAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.RuntimeState.PreparedDriverPath))
        {
            return DeploymentStepResult.Skipped("No extracted INF driver payload available.");
        }

        if (string.IsNullOrWhiteSpace(context.RuntimeState.TargetWindowsPartitionRoot))
        {
            return DeploymentStepResult.Failed("Target Windows partition is unavailable.");
        }

        string targetFoundryRoot = context.EnsureTargetFoundryRoot();
        string workingDirectory = Path.Combine(targetFoundryRoot, "Temp", "Deployment");
        string scratchDirectory = Path.Combine(targetFoundryRoot, "Temp", "Dism");

        await _windowsDeploymentService
            .ApplyOfflineDriversAsync(
                context.RuntimeState.TargetWindowsPartitionRoot,
                context.RuntimeState.PreparedDriverPath,
                scratchDirectory,
                workingDirectory,
                cancellationToken)
            .ConfigureAwait(false);

        if (context.RuntimeState.WinReConfigured &&
            !string.IsNullOrWhiteSpace(context.RuntimeState.TargetRecoveryPartitionRoot))
        {
            await _windowsDeploymentService
                .ApplyRecoveryDriversAsync(
                    context.RuntimeState.TargetRecoveryPartitionRoot,
                    context.RuntimeState.PreparedDriverPath,
                    scratchDirectory,
                    workingDirectory,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        int infCount = Directory.EnumerateFiles(context.RuntimeState.PreparedDriverPath, "*.inf", SearchOption.AllDirectories).Count();
        string driverMessage = context.RuntimeState.WinReConfigured
            ? $"Offline drivers injected into Windows and WinRE: {infCount} INF files from '{context.RuntimeState.PreparedDriverPath}'."
            : $"Offline drivers injected: {infCount} INF files from '{context.RuntimeState.PreparedDriverPath}'.";

        await context.AppendLogAsync(DeploymentLogLevel.Info, driverMessage, cancellationToken).ConfigureAwait(false);
        return DeploymentStepResult.Succeeded("Offline drivers applied.");
    }

    protected override async Task<DeploymentStepResult> ExecuteDryRunAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.RuntimeState.PreparedDriverPath) ||
            !Directory.Exists(context.RuntimeState.PreparedDriverPath))
        {
            await Task.Delay(120, cancellationToken).ConfigureAwait(false);
            return DeploymentStepResult.Skipped("No extracted INF driver payload available.");
        }

        int infCount = Directory.EnumerateFiles(context.RuntimeState.PreparedDriverPath, "*.inf", SearchOption.AllDirectories).Count();
        string driverMessage = context.RuntimeState.WinReConfigured
            ? $"[DRY-RUN] Simulated offline driver injection for Windows and WinRE: {infCount} INF files."
            : $"[DRY-RUN] Simulated offline driver injection: {infCount} INF files.";

        await context.AppendLogAsync(DeploymentLogLevel.Info, driverMessage, cancellationToken).ConfigureAwait(false);
        await Task.Delay(150, cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("Offline drivers applied (simulation).");
    }
}
