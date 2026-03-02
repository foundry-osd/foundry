using System.IO;
using Foundry.Deploy.Services.Logging;

namespace Foundry.Deploy.Services.Deployment.Steps;

public sealed class ApplyOperatingSystemImageStep : DeploymentStepBase
{
    private readonly IWindowsDeploymentService _windowsDeploymentService;

    public ApplyOperatingSystemImageStep(IWindowsDeploymentService windowsDeploymentService)
    {
        _windowsDeploymentService = windowsDeploymentService;
    }

    public override int Order => 8;

    public override string Name => DeploymentStepNames.ApplyOperatingSystemImage;

    protected override async Task<DeploymentStepResult> ExecuteLiveAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.RuntimeState.TargetWindowsPartitionRoot) ||
            string.IsNullOrWhiteSpace(context.RuntimeState.TargetSystemPartitionRoot))
        {
            return DeploymentStepResult.Failed("Target disk layout was not prepared.");
        }

        string targetFoundryRoot = context.EnsureTargetFoundryRoot();
        string imagePath = context.RuntimeState.DownloadedOperatingSystemPath ?? string.Empty;
        if (!File.Exists(imagePath))
        {
            return DeploymentStepResult.Failed("Operating system image was not downloaded.");
        }

        string workingDirectory = Path.Combine(targetFoundryRoot, "Temp", "Deployment");
        Directory.CreateDirectory(workingDirectory);

        int imageIndex = await _windowsDeploymentService
            .ResolveImageIndexAsync(imagePath, context.Request.OperatingSystem.Edition, workingDirectory, cancellationToken)
            .ConfigureAwait(false);

        context.RuntimeState.AppliedImageIndex = imageIndex;

        string scratchDirectory = Path.Combine(targetFoundryRoot, "Temp", "Dism");
        string applyStepMessage = "Applying OS image...";
        IProgress<double> applyImageProgress = context.CreateStepPercentProgressReporter(applyStepMessage, "Applying image");
        applyImageProgress.Report(0d);

        await _windowsDeploymentService
            .ApplyImageAsync(
                imagePath,
                imageIndex,
                context.RuntimeState.TargetWindowsPartitionRoot,
                scratchDirectory,
                workingDirectory,
                cancellationToken,
                applyImageProgress)
            .ConfigureAwait(false);

        await _windowsDeploymentService
            .ConfigureBootAsync(
                context.RuntimeState.TargetWindowsPartitionRoot,
                context.RuntimeState.TargetSystemPartitionRoot,
                context.Request.OperatingSystem.BuildMajor,
                workingDirectory,
                cancellationToken)
            .ConfigureAwait(false);

        try
        {
            string? appliedEdition = await _windowsDeploymentService
                .GetAppliedWindowsEditionAsync(context.RuntimeState.TargetWindowsPartitionRoot, workingDirectory, cancellationToken)
                .ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(appliedEdition))
            {
                DeploymentLogLevel editionLogLevel = EditionsMatch(context.Request.OperatingSystem.Edition, appliedEdition)
                    ? DeploymentLogLevel.Info
                    : DeploymentLogLevel.Warning;

                string message = editionLogLevel == DeploymentLogLevel.Info
                    ? $"Applied Windows edition verified: {appliedEdition}."
                    : $"Applied Windows edition '{appliedEdition}' does not closely match requested edition '{context.Request.OperatingSystem.Edition}'.";

                await context.AppendLogAsync(editionLogLevel, message, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            await context.AppendLogAsync(
                DeploymentLogLevel.Warning,
                $"Unable to verify the applied Windows edition: {ex.Message}",
                cancellationToken).ConfigureAwait(false);
        }

        await context.AppendLogAsync(
            DeploymentLogLevel.Info,
            $"OS image applied to {context.RuntimeState.TargetWindowsPartitionRoot} (index {imageIndex}); boot configured on {context.RuntimeState.TargetSystemPartitionRoot}.",
            cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("Operating system image applied.");
    }

    protected override async Task<DeploymentStepResult> ExecuteDryRunAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.RuntimeState.TargetWindowsPartitionRoot) ||
            string.IsNullOrWhiteSpace(context.RuntimeState.TargetSystemPartitionRoot))
        {
            return DeploymentStepResult.Failed("Target disk layout was not prepared.");
        }

        string targetFoundryRoot = context.EnsureTargetFoundryRoot();
        string targetRoot = Path.Combine(targetFoundryRoot, "Temp", "Deployment");
        Directory.CreateDirectory(targetRoot);
        context.RuntimeState.AppliedImageIndex = 1;

        await File.WriteAllTextAsync(
            Path.Combine(targetRoot, "apply-image.log"),
            $"Dry-run image apply at {DateTimeOffset.UtcNow:O}{Environment.NewLine}OS={context.Request.OperatingSystem.DisplayLabel}",
            cancellationToken).ConfigureAwait(false);

        await context.AppendLogAsync(DeploymentLogLevel.Info, $"[DRY-RUN] Simulated OS apply to {context.RuntimeState.TargetWindowsPartitionRoot}.", cancellationToken).ConfigureAwait(false);
        await context.AppendLogAsync(DeploymentLogLevel.Info, $"[DRY-RUN] Simulated applied Windows edition: {context.Request.OperatingSystem.Edition}.", cancellationToken).ConfigureAwait(false);
        await Task.Delay(180, cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("Operating system image applied (simulation).");
    }

    private static bool EditionsMatch(string requestedEdition, string appliedEdition)
    {
        string requested = NormalizeEditionToken(requestedEdition);
        string applied = NormalizeEditionToken(appliedEdition);

        if (requested.Length == 0 || applied.Length == 0)
        {
            return false;
        }

        return requested.Contains(applied, StringComparison.OrdinalIgnoreCase) ||
               applied.Contains(requested, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeEditionToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        char[] filtered = value
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray();

        return new string(filtered);
    }
}
