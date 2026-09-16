// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using Foundry.Core.Models.Configuration;
using Foundry.Deploy.Models;
using Foundry.Deploy.Services.Download;
using Foundry.Deploy.Services.DriverPacks;
using Foundry.Deploy.Services.Http;
using Foundry.Deploy.Services.Localization;
using Foundry.Deploy.Services.Logging;

namespace Foundry.Deploy.Services.Deployment.Steps;

/// <summary>Establishes the source and known-space prerequisites before any target partition mutation.</summary>
public sealed class PreflightDeploymentStep(
    IArtifactDownloadService artifactDownloadService,
    IWindowsDeploymentService windowsDeploymentService,
    IDeploymentStorageService storageService,
    IImageSourceProbe sourceProbe) : DeploymentStepBase
{
    public override string Name => DeploymentStepNames.PreflightDeployment;

    protected override async Task<DeploymentStepResult> ExecuteLiveAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        context.Preflight?.Dispose();
        context.Preflight = null;
        FileStream? sourceLease = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateCatalog(context.Request);
            string cacheRoot = context.RuntimeState.ResolvedCache?.RootPath
                ?? throw Guard("Preflight.NotReady", "preflight_not_ready");
            (_, DeploymentStepResult? targetFailure) = await context.TryGetValidatedTargetDiskAsync(cancellationToken).ConfigureAwait(false);
            if (targetFailure is not null) return targetFailure;

            context.EmitCurrentStepIndeterminate("Checking deployment readiness...", "Checking cache...", DeploymentOperationNames.PreflightDeployment);
            string imageDirectory = context.ResolveOperatingSystemCacheRoot();
            string fileName = DeploymentStepExecutionContext.ResolveFileName(context.Request.OperatingSystem.FileName, context.Request.OperatingSystem.Url);
            string imagePath = Path.Combine(imageDirectory, fileName);
            long sourceSize = context.Request.OperatingSystem.SizeBytes;
            bool external = await context.IsExternalStorageAsync(imageDirectory, cancellationToken).ConfigureAwait(false) &&
                context.Request.Mode == DeploymentMode.Usb;
            long? available = external ? storageService.GetAvailableBytes(imageDirectory) : null;
            long existingBytes = external && File.Exists(imagePath) ? new FileInfo(imagePath).Length : 0;
            // A replacement uses FileMode.Create, so the current archive's occupied bytes are reusable headroom.
            external = external && available is >= 0 && sourceSize > 0 &&
                sourceSize <= checked(available.Value + existingBytes) && storageService.CanWriteDirectory(imageDirectory);

            long targetDriverBytes = ResolveTargetDriverBytes(context, external ? checked(available!.Value + existingBytes - sourceSize) : null);
            DeploymentCapacityPolicy.EnsureTargetCapacity(context, null, external ? 0 : sourceSize, targetDriverBytes);
            WindowsImageMetadata? image = null;
            if (external)
            {
                ArtifactDownloadResult downloaded = await artifactDownloadService.DownloadAsync(
                    context.Request.OperatingSystem.Url, imagePath,
                    DeploymentStepExecutionContext.ResolvePreferredHash(context.Request.OperatingSystem.Sha256, context.Request.OperatingSystem.Sha1),
                    sourceSize, "OperatingSystemImage", cancellationToken,
                    context.CreateDownloadProgressReporter("OS image", DeploymentOperationNames.DownloadOperatingSystemImage)).ConfigureAwait(false);
                imagePath = downloaded.DestinationPath;
                sourceLease = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                sourceSize = sourceLease.Length;
                if (sourceSize <= 0) throw Guard("Preflight.InvalidImageMetadata", "invalid_image_metadata");
                context.EmitCurrentStepIndeterminate("Checking deployment readiness...", "Inspecting image...", DeploymentOperationNames.InspectOperatingSystemImage);
                string workingDirectory = context.ResolveWorkspaceTempPath("Deployment");
                Directory.CreateDirectory(workingDirectory);
                image = await windowsDeploymentService.InspectImageAsync(imagePath, context.Request.OperatingSystem.Edition, workingDirectory, cancellationToken).ConfigureAwait(false);
                // Use current headroom after the actual transfer, rather than trusting catalog archive sizes.
                targetDriverBytes = ResolveTargetDriverBytes(context, storageService.GetAvailableBytes(imageDirectory));
                DeploymentCapacityPolicy.EnsureTargetCapacity(context, image, 0, targetDriverBytes);
                context.RuntimeState.DownloadedOperatingSystemPath = imagePath;
            }
            else
            {
                context.EmitCurrentStepIndeterminate("Checking deployment readiness...", "Checking source access...", DeploymentOperationNames.ProbeOperatingSystemSource);
                long? advertisedSize = await sourceProbe.ProbeAsync(context.Request.OperatingSystem.Url, cancellationToken).ConfigureAwait(false);
                sourceSize = Math.Max(sourceSize, advertisedSize ?? 0);
                DeploymentCapacityPolicy.EnsureTargetCapacity(context, null, sourceSize, targetDriverBytes);
            }

            await context.AppendLogAsync(DeploymentLogLevel.Info,
                $"Preflight completed. ExternalImage={external}; ImageExpandedBytes={image?.SizeBytes.ToString() ?? "unknown"}; SourceBytes={sourceSize}; TargetDriverArchiveBytes={targetDriverBytes}; " +
                $"LayoutReserveBytes={DeploymentCapacityPolicy.LayoutReserveBytes}; ScratchAndHeadroomBytes={DeploymentCapacityPolicy.ScratchAndHeadroomBytes}; " +
                $"OptionalSetupMediaBytes={image?.SetupMediaSizeBytes?.ToString() ?? "unknown"}. " +
                "Capacity covers known residents and allowances only; driver expansion, later Microsoft Update/firmware payloads, and servicing growth remain unknown. " +
                (external ? "Available hash metadata was verified; a missing hash cannot authenticate the source." : "Source access is current only; full download, hash, edition and expanded-size checks finish after target preparation."), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            context.Preflight = new DeploymentPreflightState
            {
                CacheRoot = cacheRoot,
                UsesTargetStorage = !external,
                SourceSizeBytes = sourceSize,
                TargetDriverBytes = targetDriverBytes,
                Image = image,
                ImagePath = external ? imagePath : null,
                SourceLease = sourceLease
            };
            sourceLease = null;
            return DeploymentStepResult.Succeeded(external
                ? "Image and known capacity checked before disk preparation."
                : "Source access and known capacity checked. Full image validation finishes after disk preparation.");
        }
        catch (DeploymentOperationException exception)
        {
            return DeploymentStepResult.Failed(exception.Message, exception.Failure);
        }
        catch (OverflowException)
        {
            return Failed("Preflight.InvalidMetadata", "invalid_capacity_metadata");
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
        catch (InvalidOperationException)
        {
            return Failed("Preflight.InvalidMetadata", "invalid_source_metadata");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return DeploymentStepResult.Failed(LocalizationText.GetString("Preflight.CacheUnavailable"),
                new DeploymentFailure(DeploymentOperationNames.PreflightDeployment, DeploymentFailureKinds.Io,
                    DeploymentFailureReasons.AccessDenied, "preflight_cache_unavailable"));
        }
        finally
        {
            sourceLease?.Dispose();
        }
    }

    protected override Task<DeploymentStepResult> ExecuteDryRunAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(DeploymentStepResult.Succeeded("Deployment readiness checked (simulation)."));
    }

    private static void ValidateCatalog(DeploymentContext request)
    {
        OperatingSystemCatalogItem os = request.OperatingSystem;
        if (!Uri.TryCreate(os.Url, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) || string.IsNullOrWhiteSpace(uri.Host))
        {
            throw Guard("Preflight.InvalidSource", "invalid_source_url");
        }
        string preferredHash = DeploymentStepExecutionContext.ResolvePreferredHash(os.Sha256, os.Sha1);
        string hash = preferredHash.Replace("-", string.Empty, StringComparison.Ordinal);
        if (os.SizeBytes < 0 || request.DriverPack?.SizeBytes < 0 ||
            (preferredHash.Length > 0 && ((hash.Length != 40 && hash.Length != 64) || hash.Any(character => !Uri.IsHexDigit(character)))))
        {
            throw Guard("Preflight.InvalidMetadata", "invalid_source_metadata");
        }
        if (WindowsEditionCatalog.Find(os.Edition) is null)
        {
            throw Guard("Preflight.InvalidEdition", "invalid_image_edition");
        }
    }

    private static long ResolveTargetDriverBytes(DeploymentStepExecutionContext context, long? externalHeadroom)
    {
        DriverPackCatalogItem? driver = context.Request.DriverPack;
        if (driver is null || context.Request.DriverPackSelectionKind is DriverPackSelectionKind.None or DriverPackSelectionKind.MicrosoftUpdateCatalog)
        {
            return 0;
        }
        string fileName = DeploymentStepExecutionContext.ResolveFileName(driver.FileName, driver.DownloadUrl);
        DriverPackExecutionPlan plan = new DriverPackStrategyResolver().Resolve(context.Request.DriverPackSelectionKind, driver, fileName);
        long archive = externalHeadroom.HasValue && externalHeadroom.Value >= driver.SizeBytes ? 0 : driver.SizeBytes;
        return checked(archive + (plan.InstallMode == DriverPackInstallMode.DeferredSetupComplete ? driver.SizeBytes : 0));
    }

    /// <summary>Creates localized validation failures before allowing any destructive boundary.</summary>
    internal static DeploymentOperationException Guard(string key, string code) => new(
        DeploymentFailure.Guard(DeploymentOperationNames.PreflightDeployment, DeploymentFailureReasons.InvalidInput, code), LocalizationText.GetString(key));

    private static DeploymentStepResult Failed(string key, string code)
    {
        DeploymentOperationException exception = Guard(key, code);
        return DeploymentStepResult.Failed(exception.Message, exception.Failure);
    }
}
