// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using Foundry.Deploy.Services.Download;
using Foundry.Deploy.Services.Http;
using Foundry.Deploy.Services.Localization;
using Foundry.Deploy.Services.Logging;

namespace Foundry.Deploy.Services.Deployment.Steps;

public sealed class DownloadOperatingSystemImageStep : DeploymentStepBase
{
    private readonly IArtifactDownloadService _artifactDownloadService;

    public DownloadOperatingSystemImageStep(IArtifactDownloadService artifactDownloadService)
    {
        _artifactDownloadService = artifactDownloadService;
    }

    public override string Name => DeploymentStepNames.DownloadOperatingSystemImage;

    protected override async Task<DeploymentStepResult> ExecuteLiveAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        try
        {
            return await DownloadImageAsync(context, cancellationToken).ConfigureAwait(false);
        }
        catch (DeploymentOperationException exception)
        {
            return DeploymentStepResult.Failed(exception.Message, exception.Failure);
        }
        catch (HttpRequestException exception)
        {
            return DeploymentStepResult.Failed(HttpConnectionFailure.IsSecureConnectionFailure(exception)
                ? HttpConnectionFailure.SecureConnectionMessage : LocalizationText.GetString("Preflight.SourceUnavailable"),
                DeploymentFailureClassifier.Classify(exception, DeploymentOperationNames.DownloadOperatingSystemImage));
        }
        catch (CryptographicException exception)
        {
            return DeploymentStepResult.Failed(LocalizationText.GetString("Preflight.InvalidMetadata"),
                DeploymentFailureClassifier.Classify(exception, DeploymentOperationNames.DownloadOperatingSystemImage));
        }
        catch (InvalidOperationException exception)
        {
            return DeploymentStepResult.Failed(LocalizationText.GetString("Preflight.InvalidMetadata"),
                DeploymentFailureClassifier.Classify(exception, DeploymentOperationNames.DownloadOperatingSystemImage));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return DeploymentStepResult.Failed(LocalizationText.GetString("Preflight.CacheUnavailable"),
                DeploymentFailureClassifier.Classify(exception, DeploymentOperationNames.DownloadOperatingSystemImage));
        }
    }

    private async Task<DeploymentStepResult> DownloadImageAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        DeploymentPreflightState? prepared = context.Preflight;
        if (prepared is null || !prepared.MatchesStoragePlan(context))
        {
            throw PreflightDeploymentStep.Guard("Preflight.NotReady", "preflight_not_ready");
        }

        if (prepared is { UsesTargetStorage: false } &&
            (string.IsNullOrWhiteSpace(prepared.ExternalImageDirectory) ||
             !await context.IsExternalStorageAsync(prepared.ExternalImageDirectory, cancellationToken).ConfigureAwait(false)))
        {
            throw PreflightDeploymentStep.Guard("Preflight.NotReady", "preflight_not_ready");
        }

        string osDirectory = prepared.UsesTargetStorage
            ? Path.Combine(context.EnsureTargetFoundryRoot(), "Cache", "OperatingSystems")
            : prepared.ExternalImageDirectory!;
        Directory.CreateDirectory(osDirectory);
        const string stepMessage = "Downloading OS image...";

        string fileName = DeploymentStepExecutionContext.ResolveFileName(
            context.Request.OperatingSystem.FileName,
            context.Request.OperatingSystem.Url);
        string destinationPath = Path.Combine(osDirectory, fileName);
        string? expectedOsHash = DeploymentStepExecutionContext.ResolvePreferredHash(
            context.Request.OperatingSystem.Sha256,
            context.Request.OperatingSystem.Sha1);

        context.EmitCurrentStepIndeterminate(
            stepMessage,
            "Checking cache...",
            DeploymentOperationNames.DownloadOperatingSystemImage);
        IProgress<DownloadProgress> osDownloadProgress = context.CreateDownloadProgressReporter(
            "OS image",
            DeploymentOperationNames.DownloadOperatingSystemImage);
        ArtifactDownloadResult result = await _artifactDownloadService
            .DownloadAsync(
                context.Request.OperatingSystem.Url,
                destinationPath,
                expectedHash: expectedOsHash,
                expectedSizeBytes: context.Request.OperatingSystem.SizeBytes,
                artifactKind: "OperatingSystemImage",
                cancellationToken: cancellationToken,
                progress: osDownloadProgress)
            .ConfigureAwait(false);

        context.RuntimeState.DownloadedOperatingSystemPath = result.DestinationPath;
        prepared.Image = null;
        prepared.ImagePath = result.DestinationPath;
        prepared.SourceLease?.Dispose();
        prepared.SourceLease = prepared.UsesTargetStorage
            ? null
            : new FileStream(result.DestinationPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        prepared.SourceSizeBytes = prepared.SourceLease?.Length ?? new FileInfo(result.DestinationPath).Length;
        await context.AppendLogAsync(
            DeploymentLogLevel.Info,
            $"OS image {(result.Downloaded ? "downloaded" : "reused")} via {result.Method}: {result.DestinationPath}",
            cancellationToken).ConfigureAwait(false);

        return result.Downloaded
            ? DeploymentStepResult.Succeeded("Operating system image downloaded.")
            : DeploymentStepResult.Skipped("Operating system image resolved from cache.");
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
