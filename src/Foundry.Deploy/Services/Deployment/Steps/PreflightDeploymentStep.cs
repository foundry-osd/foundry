// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Net.Http;
using Foundry.Core.Models.Configuration;
using Foundry.Deploy.Models;
using Foundry.Deploy.Services.DriverPacks;
using Foundry.Deploy.Services.Http;
using Foundry.Deploy.Services.Localization;
using Foundry.Deploy.Services.Logging;

namespace Foundry.Deploy.Services.Deployment.Steps;

/// <summary>Chooses safe image staging and checks source access and known capacity before image transfer or target mutation.</summary>
public sealed class PreflightDeploymentStep(
    IDeploymentStorageService storageService,
    IImageSourceProbe sourceProbe,
    Foundry.Core.Services.Images.ICustomImageMetadataReader? customMetadataReader = null) : DeploymentStepBase
{
    public override string Name => DeploymentStepNames.PreflightDeployment;

    protected override async Task<DeploymentStepResult> ExecuteLiveAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        context.Preflight?.Dispose();
        context.Preflight = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (context.Request.OperatingSystem is CustomImageSelection custom)
                return await PrepareCustomImageAsync(context, custom, cancellationToken).ConfigureAwait(false);
            ValidateCatalog(context.Request);
            string cacheRoot = context.RuntimeState.ResolvedCache?.RootPath
                ?? throw Guard("Preflight.NotReady", "preflight_not_ready");
            (_, DeploymentStepResult? targetFailure) = await context.TryGetValidatedTargetDiskAsync(cancellationToken).ConfigureAwait(false);
            if (targetFailure is not null) return targetFailure;

            context.EmitCurrentStepIndeterminate("Checking deployment readiness...", "Checking cache...", DeploymentOperationNames.PreflightDeployment);
            string imageDirectory = context.ResolveOperatingSystemCacheRoot();
            string fileName = DeploymentStepExecutionContext.ResolveFileName(context.Request.OperatingSystem.FileName, (context.Request.OperatingSystem as OperatingSystemCatalogItem)?.Url ?? string.Empty);
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
            if (!external)
            {
                context.EmitCurrentStepIndeterminate("Checking deployment readiness...", "Checking source access...", DeploymentOperationNames.ProbeOperatingSystemSource);
                long? advertisedSize = await sourceProbe.ProbeAsync((context.Request.OperatingSystem as OperatingSystemCatalogItem)?.Url ?? string.Empty, cancellationToken).ConfigureAwait(false);
                sourceSize = Math.Max(sourceSize, advertisedSize ?? 0);
                DeploymentCapacityPolicy.EnsureTargetCapacity(context, null, sourceSize, targetDriverBytes);
            }

            await context.AppendLogAsync(DeploymentLogLevel.Info,
                $"Storage preparation completed. ExternalImage={external}; SourceBytes={sourceSize}; TargetDriverArchiveBytes={targetDriverBytes}; " +
                $"LayoutReserveBytes={DeploymentCapacityPolicy.LayoutReserveBytes}; ScratchAndHeadroomBytes={DeploymentCapacityPolicy.ScratchAndHeadroomBytes}; " +
                "Capacity covers known residents and allowances only; driver expansion, later Microsoft Update/firmware payloads, and servicing growth remain unknown. " +
                (external ? "Image download and validation must complete before target preparation." : "Source access is current only; full download, hash, edition and expanded-size checks finish after target preparation."), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            context.Preflight = new DeploymentPreflightState
            {
                CacheRoot = cacheRoot,
                UsesTargetStorage = !external,
                SourceSizeBytes = sourceSize,
                TargetDriverBytes = targetDriverBytes,
                ExternalImageDirectory = external ? imageDirectory : null
            };
            return DeploymentStepResult.Succeeded(external
                ? "External image storage and known capacity checked."
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
        catch (InvalidOperationException)
        {
            return Failed("Preflight.InvalidMetadata", "invalid_source_metadata");
        }
        catch (Exception exception) when (exception is InvalidDataException or global::System.Runtime.InteropServices.COMException)
        {
            return Failed("CustomImages.InvalidSource", "invalid_custom_image");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return DeploymentStepResult.Failed(LocalizationText.GetString(context.Request.OperatingSystem is CustomImageSelection ? "CustomImages.InvalidSource" : "Preflight.CacheUnavailable"),
                new DeploymentFailure(DeploymentOperationNames.PreflightDeployment, DeploymentFailureKinds.Io,
                    DeploymentFailureReasons.AccessDenied, "preflight_cache_unavailable"));
        }
    }

    private async Task<DeploymentStepResult> PrepareCustomImageAsync(DeploymentStepExecutionContext context, CustomImageSelection custom, CancellationToken cancellationToken)
    {
        (_, DeploymentStepResult? targetFailure) = await context.TryGetValidatedTargetDiskAsync(cancellationToken).ConfigureAwait(false);
        if (targetFailure is not null) return targetFailure;
        CustomImageAsset asset = custom.Asset;
        if (!await context.IsCustomSourceSeparateAsync(asset, asset.ImagePath, cancellationToken).ConfigureAwait(false))
            throw Guard("CustomImages.UnsafeSource", "custom_source_identity_unknown");
        var prepared = new DeploymentPreflightState
        {
            CacheRoot = context.RuntimeState.ResolvedCache?.RootPath ?? throw Guard("Preflight.NotReady", "preflight_not_ready"),
            UsesTargetStorage = false,
            ImagePath = asset.ImagePath,
            ExternalImageDirectory = Path.GetDirectoryName(asset.ImagePath)
        };
        try
        {
            context.EmitCurrentStepIndeterminate("Checking Windows image...", "Verifying custom image...", DeploymentOperationNames.InspectOperatingSystemImage);
            prepared.CustomSourceLease = await Services.Images.CustomImageSourceLease.AcquireAsync(asset.ImagePath,
                asset.ObservedLength ?? asset.ExpectedLength, asset.ObservedHash ?? asset.ExpectedHash, cancellationToken).ConfigureAwait(false);
            var images = await (customMetadataReader ?? new Foundry.Core.Services.Images.NativeCustomImageMetadataReader())
                .ReadAsync(asset.ImagePath, cancellationToken).ConfigureAwait(false);
            var matches = images.Where(image => image.Index == custom.Index.Index).ToArray();
            if (matches.Length != 1 || matches[0].ExpandedSizeBytes <= 0) throw new InvalidDataException();
            var selected = matches[0];
            if (selected.EditionId != custom.Edition || selected.Architecture != custom.Architecture || selected.ExpandedSizeBytes != custom.Index.ExpandedSizeBytes ||
                selected.Build != custom.Index.Build || selected.Version != custom.Index.Version) throw new InvalidDataException();
            if (context.Request.TargetDiskIdentity?.SizeBytes is null or 0) throw new InvalidDataException();
            var setupImages = images.Where(image => image.Name.Equals("Windows Setup Media", StringComparison.OrdinalIgnoreCase)).ToArray();
            long? setupSize = setupImages.Length == 1 && setupImages[0].ExpandedSizeBytes > 0 ? setupImages[0].ExpandedSizeBytes : null;
            prepared.Image = new WindowsImageMetadata(selected.Index, selected.EditionId, selected.ExpandedSizeBytes, setupSize);
            prepared.SourceSizeBytes = prepared.CustomSourceLease.Length;
            prepared.TargetDriverBytes = ResolveTargetDriverBytes(context, null);
            DeploymentCapacityPolicy.EnsureTargetCapacity(context, prepared.Image, 0, prepared.TargetDriverBytes);
            context.RuntimeState.DownloadedOperatingSystemPath = asset.ImagePath;
            context.Preflight = prepared;
            return DeploymentStepResult.Succeeded("Custom image verified before target preparation.");
        }
        catch { prepared.Dispose(); throw; }
    }

    protected override Task<DeploymentStepResult> ExecuteDryRunAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        context.Preflight?.Dispose();
        context.Preflight = new DeploymentPreflightState
        {
            CacheRoot = context.RuntimeState.ResolvedCache?.RootPath ?? context.Request.CacheRootPath,
            UsesTargetStorage = true
        };
        return Task.FromResult(DeploymentStepResult.Succeeded("Deployment readiness checked (simulation)."));
    }

    private static void ValidateCatalog(DeploymentContext request)
    {
        OperatingSystemCatalogItem os = (OperatingSystemCatalogItem)request.OperatingSystem;
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

    /// <summary>Budgets archives and deferred installer copies using current external cache headroom.</summary>
    internal static long ResolveTargetDriverBytes(DeploymentStepExecutionContext context, long? externalHeadroom)
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
