// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.IO;
using Foundry.Core.Models.Configuration;
using Foundry.Deploy.Models;
using Foundry.Deploy.Services.Cache;
using Foundry.Deploy.Services.Download;
using Foundry.Utilities.Processes;

namespace Foundry.Deploy.Services.Deployment;

/// <summary>Verifies independent image bytes when feasible and explicitly limits target-backed preflight to metadata.</summary>
public sealed class DeploymentPreflightService(IArtifactDownloadService downloads,
    IWindowsImageInspectionService inspection, IArtifactAvailabilityProbe availability, IVolumeStorageProbe storage)
{
    public const long IndependentTransferReserveBytes = 64L * 1024 * 1024;

    /// <summary>Rejects unsupported or unauthenticated image selections before storage or network access.</summary>
    public static void ValidateSelection(OperatingSystemCatalogItem selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        OperatingSystemReleaseDefinition? release = OperatingSystemSelectionCatalog.FindRelease(selection.ReleaseId);
        string architecture = WindowsImageInfoParser.NormalizeArchitecture(selection.Architecture);
        if (!OperatingSystemSupportMatrix.IsSupported(selection) || release?.Build != selection.BuildMajor ||
            selection.BuildUbr < 0 || !WindowsEditionCatalog.IsSupported(selection.Edition, selection.LicenseChannel, architecture) ||
            !CultureInfo.GetCultures(CultureTypes.SpecificCultures).Any(culture =>
                culture.Name.Equals(selection.LanguageCode, StringComparison.OrdinalIgnoreCase)) ||
            Path.GetExtension(selection.FileName).ToLowerInvariant() is not (".esd" or ".wim"))
        {
            throw new InvalidDataException("The selected Windows image release, edition, architecture, build, language or file format is unsupported.");
        }
        ArtifactIntegrityPolicy.FromOperatingSystem(selection);
    }

    /// <summary>The caller must prove that a supplied cache root is independent of the confirmed target disk.</summary>
    public async Task<DeploymentPreflightResult> PrepareAsync(DeploymentContext request, TargetDiskInfo target,
        string? independentCacheRoot, string workingDirectory, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ValidateSelection(request.OperatingSystem);
        ArtifactIdentity artifact = ArtifactIntegrityPolicy.FromOperatingSystem(request.OperatingSystem);
        string constraint = "No storage independent of the target disk is available.";

        if (!string.IsNullOrWhiteSpace(independentCacheRoot))
        {
            RejectReparseAncestors(independentCacheRoot);
            string directory = Path.Combine(independentCacheRoot, artifact.CacheKey);
            string destination = Path.Combine(directory, artifact.FileName);
            foreach (string path in new[] { destination, Path.Combine(independentCacheRoot, artifact.FileName) })
            {
                DeploymentPreflightResult? cached = await TryInspectCachedAsync(request, target, artifact, path, workingDirectory, token)
                    .ConfigureAwait(false);
                if (cached is not null) return cached;
            }

            if (artifact.Integrity.SizeBytes is long sourceSize)
            {
                EnsureTargetCapacity(request, target, null, usesTargetStorage: false);
                long requiredIndependentBytes = checked(sourceSize + IndependentTransferReserveBytes);
                VolumeStorageStatus status;
                try
                {
                    RejectReparseAncestors(directory);
                    status = storage.Inspect(directory);
                }
                catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
                {
                    status = new(false, false, null);
                }
                constraint = !status.IsPresent ? "Independent storage is unavailable." :
                    !status.IsWritable ? "Independent storage is not writable." :
                    status.FreeBytes is null ? "Independent storage capacity is unknown." :
                    status.FreeBytes < requiredIndependentBytes ? "Independent storage cannot hold the image and transfer reserve." : string.Empty;
                if (constraint.Length == 0)
                {
                    await downloads.DownloadAsync(artifact, destination, token).ConfigureAwait(false);
                    return await TryInspectCachedAsync(request, target, artifact, destination, workingDirectory, token).ConfigureAwait(false)
                        ?? throw new InvalidDataException("The acquired Windows image failed verification before inspection.");
                }
            }
            else
            {
                constraint = "The compressed image size is unknown; independent acquisition cannot be capacity-checked.";
            }
        }

        long requiredTargetBytes = EnsureTargetCapacity(request, target, null, usesTargetStorage: true);
        await availability.EnsureAvailableAsync(artifact, token).ConfigureAwait(false);
        return new(ImagePreflightLevel.TargetBackedMetadataOnly, requiredTargetBytes, null, null,
            constraint + " Only authenticated metadata and source availability were checked; image download, hashing and inspection must complete after target preparation.")
        { Selection = request.OperatingSystem };
    }

    private async Task<DeploymentPreflightResult?> TryInspectCachedAsync(DeploymentContext request, TargetDiskInfo target,
        ArtifactIdentity artifact, string path, string workingDirectory, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        RejectReparseAncestors(path);
        if (!File.Exists(path)) return null;
        using NativeFileLease lease = NativeFileLease.OpenRead(path);
        if (await downloads.TryUseCachedAsync(artifact, path, token).ConfigureAwait(false) is null) return null;
        WindowsImageInfo image = await lease.RunAsync(
            () => inspection.InspectImageAsync(path, request.OperatingSystem, workingDirectory, token), token).ConfigureAwait(false);
        ValidateImage(request.OperatingSystem, image);
        long required = EnsureTargetCapacity(request, target, image, usesTargetStorage: false);
        return new(ImagePreflightLevel.CompleteImageVerified, required, path, image, null) { Selection = request.OperatingSystem };
    }

    private static void ValidateImage(OperatingSystemCatalogItem selection, WindowsImageInfo image)
    {
        WindowsEditionDefinition edition = WindowsEditionCatalog.Find(selection.Edition)!;
        if (image.Index <= 0 || !image.EditionId.Equals(edition.EditionId, StringComparison.OrdinalIgnoreCase) ||
            !image.Architecture.Equals(WindowsImageInfoParser.NormalizeArchitecture(selection.Architecture), StringComparison.OrdinalIgnoreCase) ||
            image.Version.Major != 10 || image.Version.Minor != 0 || image.Version.Build != selection.BuildMajor ||
            image.Version.Revision != selection.BuildUbr || !image.DefaultLanguage.Equals(selection.LanguageCode, StringComparison.OrdinalIgnoreCase) ||
            image.ExpandedSizeBytes <= 0)
        {
            throw new InvalidDataException("The inspected Windows image does not match the selected catalog identity.");
        }
    }

    private static long EnsureTargetCapacity(DeploymentContext request, TargetDiskInfo target, WindowsImageInfo? image, bool usesTargetStorage)
    {
        long selectedPayloadBytes = request.DriverPackSelectionKind == DriverPackSelectionKind.OemCatalog ? request.DriverPack?.SizeBytes ?? 0 : 0;
        long required = DeploymentPreflightCapacityPolicy.CalculateRequiredTargetBytes(request.OperatingSystem, image, usesTargetStorage, selectedPayloadBytes);
        if (target.SizeBytes < (ulong)required)
            throw new InvalidDataException($"The target disk is too small for the selected image and reserves. Required bytes: {required}; target bytes: {target.SizeBytes}.");
        return required;
    }

    private static void RejectReparseAncestors(string path)
    {
        for (string? current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Independent image storage must not traverse a reparse point.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
}
