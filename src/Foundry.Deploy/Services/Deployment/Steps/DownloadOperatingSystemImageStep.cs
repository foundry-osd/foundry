using System.IO;
using Foundry.Deploy.Services.Download;
using Foundry.Deploy.Services.Logging;

namespace Foundry.Deploy.Services.Deployment.Steps;

public sealed class DownloadOperatingSystemImageStep : DeploymentStepBase
{
    private readonly IArtifactDownloadService _artifactDownloadService;

    public DownloadOperatingSystemImageStep(IArtifactDownloadService artifactDownloadService)
    {
        _artifactDownloadService = artifactDownloadService;
    }

    public override int Order => 6;

    public override string Name => DeploymentStepNames.DownloadOperatingSystemImage;

    protected override async Task<DeploymentStepResult> ExecuteLiveAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        string osDirectory = context.ResolveOperatingSystemCacheRoot(context.Request.OperatingSystem.SizeBytes);
        Directory.CreateDirectory(osDirectory);
        const string stepMessage = "Downloading OS image...";

        string fileName = DeploymentStepExecutionContext.ResolveFileName(
            context.Request.OperatingSystem.FileName,
            context.Request.OperatingSystem.Url);
        string destinationPath = Path.Combine(osDirectory, fileName);
        string? expectedOsHash = DeploymentStepExecutionContext.ResolvePreferredHash(
            context.Request.OperatingSystem.Sha256,
            context.Request.OperatingSystem.Sha1);

        context.EmitCurrentStepIndeterminate(stepMessage, "Checking cache...");
        IProgress<DownloadProgress> osDownloadProgress = context.CreateDownloadProgressReporter("OS image");
        ArtifactDownloadResult result = await _artifactDownloadService
            .DownloadAsync(
                context.Request.OperatingSystem.Url,
                destinationPath,
                expectedHash: expectedOsHash,
                cancellationToken: cancellationToken,
                progress: osDownloadProgress)
            .ConfigureAwait(false);

        context.RuntimeState.DownloadedOperatingSystemPath = result.DestinationPath;
        await context.AppendLogAsync(
            DeploymentLogLevel.Info,
            $"OS image {(result.Downloaded ? "downloaded" : "reused")} via {result.Method}: {result.DestinationPath}",
            cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded(
            result.Downloaded
                ? "Operating system image downloaded."
                : "Operating system image resolved from cache.");
    }

    protected override async Task<DeploymentStepResult> ExecuteDryRunAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        string osDirectory = context.ResolveOperatingSystemCacheRoot();
        Directory.CreateDirectory(osDirectory);

        string fileName = DeploymentStepExecutionContext.ResolveFileName(
            context.Request.OperatingSystem.FileName,
            context.Request.OperatingSystem.Url);
        string simulatedPath = Path.Combine(osDirectory, $"{fileName}.dryrun.txt");
        await File.WriteAllTextAsync(
            simulatedPath,
            $"Dry-run artifact created at {DateTimeOffset.UtcNow:O}{Environment.NewLine}SourceUrl={context.Request.OperatingSystem.Url}",
            cancellationToken).ConfigureAwait(false);

        context.RuntimeState.DownloadedOperatingSystemPath = simulatedPath;
        await context.AppendLogAsync(DeploymentLogLevel.Info, $"[DRY-RUN] Simulated OS artifact: {simulatedPath}", cancellationToken).ConfigureAwait(false);
        await Task.Delay(150, cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("Operating system image ready (simulation).");
    }
}
