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

using Foundry.Deploy.Models;

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
        catch (ArtifactHashMismatchException exception)
        {
            return DeploymentStepResult.Failed(LocalizationText.GetString("Preflight.DownloadVerificationFailed"),
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
        if (context.Request.OperatingSystem is CustomImageSelection custom)
        {
            if (context.Preflight?.CustomSourceLease is null || !context.Preflight.Matches(context))
                return DeploymentStepResult.Failed("Custom image is not ready.", DeploymentFailure.Guard(DeploymentOperationNames.PreflightDeployment,
                    DeploymentFailureReasons.InvalidState, "preflight_not_ready"));
            return context.Preflight.UsesTargetStorage
                ? await CopyCustomImageToTargetAsync(context, custom, context.Preflight, cancellationToken).ConfigureAwait(false)
                : DeploymentStepResult.Skipped("Custom image resolved from deployment media.");
        }
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
            (context.Request.OperatingSystem as OperatingSystemCatalogItem)?.Url ?? string.Empty);
        string destinationPath = Path.Combine(osDirectory, fileName);
        string? expectedOsHash = DeploymentStepExecutionContext.ResolvePreferredHash(
            (context.Request.OperatingSystem as OperatingSystemCatalogItem)?.Sha256 ?? string.Empty,
            (context.Request.OperatingSystem as OperatingSystemCatalogItem)?.Sha1 ?? string.Empty);

        context.EmitCurrentStepIndeterminate(
            stepMessage,
            "Checking cache...",
            DeploymentOperationNames.DownloadOperatingSystemImage);
        IProgress<DownloadProgress> osDownloadProgress = context.CreateDownloadProgressReporter(
            "OS image",
            DeploymentOperationNames.DownloadOperatingSystemImage);
        ArtifactDownloadResult result = await _artifactDownloadService
            .DownloadAsync(
                (context.Request.OperatingSystem as OperatingSystemCatalogItem)?.Url ?? string.Empty,
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

    /// <summary>
    /// Copies a network cache share custom image to the target operating system cache through the preflight lease, hashing it in
    /// the same pass, so the share is read once. A length or digest mismatch fails the deployment; the target was already prepared,
    /// which is the accepted trade-off for share content. On success the lease moves to the local copy, which is then applied.
    /// </summary>
    private static async Task<DeploymentStepResult> CopyCustomImageToTargetAsync(DeploymentStepExecutionContext context, CustomImageSelection custom,
        DeploymentPreflightState prepared, CancellationToken cancellationToken)
    {
        string sourcePath = prepared.ImagePath!;
        string osDirectory = Path.Combine(context.EnsureTargetFoundryRoot(), "Cache", "OperatingSystems");
        string destinationPath = Path.Combine(osDirectory, Path.GetFileName(sourcePath));
        long sourceBytes = prepared.SourceSizeBytes;
        try
        {
            Directory.CreateDirectory(osDirectory);
            context.EmitCurrentStepIndeterminate("Copying custom image...", "Copying from the network cache...", DeploymentOperationNames.DownloadOperatingSystemImage);
            IProgress<DownloadProgress> progress = context.CreateDownloadProgressReporter("custom image", DeploymentOperationNames.DownloadOperatingSystemImage,
                "Copying custom image...");
            await prepared.CustomSourceLease!.CopyVerifiedToAsync(destinationPath, custom.Asset.ObservedHash ?? custom.Asset.ExpectedHash!,
                progress, cancellationToken).ConfigureAwait(false);
            // Protects the verified local copy against replacement until it is applied. The copy closes its writer before this
            // read lease opens, so the file is unprotected for that moment. That window is accepted: only WinPE processes, which
            // Deploy runs, can reach the freshly prepared target, and the lease checks the length again when it opens.
            var local = Services.Images.CustomImageSourceLease.OpenWithoutHashing(destinationPath, sourceBytes);
            prepared.CustomSourceLease.Dispose();
            prepared.CustomSourceLease = local;
            prepared.ImagePath = destinationPath;
            context.RuntimeState.DownloadedOperatingSystemPath = destinationPath;
        }
        catch (OperationCanceledException)
        {
            prepared.CustomSourceLease?.Dispose();
            throw;
        }
        catch (InvalidDataException exception)
        {
            // Releasing the share lease leaves no readiness from which the unverified share image could still be applied.
            prepared.CustomSourceLease?.Dispose();
            await context.AppendLogAsync(DeploymentLogLevel.Error,
                $"Custom image from the network cache share failed verification during the copy: {sourcePath} ({exception.Message})", CancellationToken.None).ConfigureAwait(false);
            return DeploymentStepResult.Failed(LocalizationText.GetString("CustomImages.InvalidSource"),
                DeploymentFailure.Guard(DeploymentOperationNames.DownloadOperatingSystemImage, DeploymentFailureReasons.InvalidInput, "invalid_custom_image"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or TimeoutException)
        {
            prepared.CustomSourceLease?.Dispose();
            await context.AppendLogAsync(DeploymentLogLevel.Error,
                $"Custom image copy from the network cache share failed: {sourcePath} ({exception.GetType().Name})", CancellationToken.None).ConfigureAwait(false);
            return DeploymentStepResult.Failed(LocalizationText.GetString("CustomImages.InvalidSource"),
                DeploymentFailureClassifier.Classify(exception, DeploymentOperationNames.DownloadOperatingSystemImage));
        }

        await context.AppendLogAsync(DeploymentLogLevel.Info,
            $"Custom image copied from the network cache share and verified: {sourcePath} -> {destinationPath} ({sourceBytes} bytes).", cancellationToken).ConfigureAwait(false);
        return DeploymentStepResult.Succeeded("Custom image copied from the network cache share.");
    }

    protected override async Task<DeploymentStepResult> ExecuteDryRunAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        if (context.Request.OperatingSystem is CustomImageSelection custom)
        {
            context.RuntimeState.DownloadedOperatingSystemPath = custom.Asset.ImagePath;
            return DeploymentStepResult.Skipped("Custom image resolution simulated.");
        }
        string osDirectory = context.ResolveOperatingSystemCacheRoot();
        Directory.CreateDirectory(osDirectory);

        string fileName = DeploymentStepExecutionContext.ResolveFileName(
            context.Request.OperatingSystem.FileName,
            (context.Request.OperatingSystem as OperatingSystemCatalogItem)?.Url ?? string.Empty);
        string simulatedPath = Path.Combine(osDirectory, $"{fileName}.dryrun.txt");
        await File.WriteAllTextAsync(
            simulatedPath,
            $"Dry-run artifact created at {DateTimeOffset.UtcNow:O}{Environment.NewLine}SourceUrl={(context.Request.OperatingSystem as OperatingSystemCatalogItem)?.Url ?? string.Empty}",
            cancellationToken).ConfigureAwait(false);

        context.RuntimeState.DownloadedOperatingSystemPath = simulatedPath;
        await context.AppendLogAsync(DeploymentLogLevel.Info, $"[DRY-RUN] Simulated OS artifact: {simulatedPath}", cancellationToken).ConfigureAwait(false);
        await Task.Delay(150, cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("Operating system image ready (simulation).");
    }
}
