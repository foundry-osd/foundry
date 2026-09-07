// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Foundry.Core.Services.Catalog;
using Foundry.Core.Services.WinPe;
using Foundry.Deploy.Models;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Autopilot;
using Foundry.Deploy.Services.Catalog;
using Foundry.Deploy.Services.Configuration;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.Download;
using Foundry.Deploy.Services.Security;
using Foundry.Deploy.Services.Deployment.Unattend;

namespace Foundry.Deploy.Services.Startup;

/// <summary>Reconstructs offline evidence from the trusted boot image and current selected artifacts.</summary>
public sealed class DeploymentOfflineWorkflow(IArtifactDownloadService downloads,
    IAutopilotProfileContentService? profiles = null, IDeploymentSecretKeySession? secretSession = null,
    UnattendContentService? unattendContent = null)
{
    public const string TrustedRoot = @"X:\";
    public static string RuntimeIdentifier => "win-" + RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();

    public static async Task<DeploymentCatalogSnapshot> LoadCatalogsAsync(bool requireDrivers, CancellationToken token)
    {
        WinPeMediaManifest manifest = await WinPeMediaManifestStore.ReadAsync(Path.Combine(TrustedRoot, WinPeMediaManifestStore.RelativePath), token).ConfigureAwait(false);
        WinPeMediaManifestStore.Validate(manifest, RuntimeIdentifier);
        var store = new CatalogSnapshotStore(TrustedRoot, manifest);
        return await new DeploymentCatalogLoadService(new VerifiedCatalogSnapshotAcquirer(), store)
            .LoadAsync(new(true, requireDrivers), token).ConfigureAwait(false);
    }

    public async Task<DeploymentOfflineReadinessResult> EvaluateAsync(OperatingSystemCatalogItem? operatingSystem,
        DriverPackCatalogItem? driverPack, string cacheRoot, IReadOnlyList<string> requestedOnlineCapabilities,
        CancellationToken token, string configurationPath = DeployConfigurationService.DefaultConfigurationPath)
    {
        DeploymentOfflineConfigurationSnapshot snapshot = DeploymentOfflineConfigurationSnapshot.Parse(
            await ReadConfigurationAsync(configurationPath, token).ConfigureAwait(false));
        return await EvaluateSnapshotAsync(snapshot, operatingSystem, driverPack, cacheRoot,
            requestedOnlineCapabilities, token, configurationPath).ConfigureAwait(false);
    }

    internal async Task<DeploymentOfflineReadinessResult> EvaluateSnapshotAsync(DeploymentOfflineConfigurationSnapshot snapshot,
        OperatingSystemCatalogItem? operatingSystem, DriverPackCatalogItem? driverPack, string cacheRoot,
        IReadOnlyList<string> requestedOnlineCapabilities, CancellationToken token, string configurationPath,
        DeploymentContext? currentRequest = null)
    {
        WinPeMediaManifest manifest = await WinPeMediaManifestStore.ReadAsync(Path.Combine(TrustedRoot, WinPeMediaManifestStore.RelativePath), token).ConfigureAwait(false);
        WinPeMediaManifestStore.Validate(manifest, RuntimeIdentifier);
        var catalogs = await new DeploymentCatalogLoadService(new VerifiedCatalogSnapshotAcquirer(), new CatalogSnapshotStore(TrustedRoot, manifest))
            .LoadAsync(new(true, driverPack is not null), token).ConfigureAwait(false);
        if (catalogs.OperatingSystemSnapshot is not { IsOffline: true } operatingSystems ||
            !operatingSystems.Items.Any(item => "win-" + item.Architecture == RuntimeIdentifier))
            throw new InvalidDataException("No compatible operating systems are available in this media's verified local catalog.");
        List<string> blockers = [.. requestedOnlineCapabilities];
        if (currentRequest is null)
            await ValidateLocalCustomizationAsync(snapshot.Document, configurationPath, blockers, token).ConfigureAwait(false);
        else
            blockers.AddRange(await ValidateCurrentCustomizationAsync(currentRequest, snapshot.Document, token).ConfigureAwait(false));
        List<OfflineArtifactRequirement> artifacts = [];
        if (operatingSystem is not null) artifacts.Add(Requirement(ArtifactIntegrityPolicy.FromOperatingSystem(operatingSystem), cacheRoot, "OperatingSystems"));
        if (driverPack is not null) artifacts.Add(DriverRequirement(driverPack, cacheRoot));
        return await new DeploymentOfflineReadinessService(downloads).EvaluateAsync(new()
        {
            TrustedManifest = manifest,
            ExpectedMediaId = manifest.MediaId,
            RuntimeIdentifier = RuntimeIdentifier,
            ConfigurationBytes = snapshot.CopyContent(),
            ExpectedConfigurationDigest = snapshot.Digest,
            Catalogs = catalogs,
            OperatingSystem = operatingSystem,
            SelectedDriverPacks = driverPack is null ? [] : [driverPack],
            RequiredArtifacts = artifacts,
            OnlineOnlyCapabilities = blockers,
            SelectionsResolved = operatingSystem is not null
        }, token).ConfigureAwait(false);
    }

    public async Task<DeploymentOfflineReadinessResult> EvaluateAsync(DeploymentContext request, CancellationToken token)
    {
        List<string> blockers = [];
        if (request.DriverPackSelectionKind == DriverPackSelectionKind.MicrosoftUpdateCatalog) blockers.Add("Selected Microsoft Update drivers require online mode.");
        if (request.ApplyFirmwareUpdates) blockers.Add("Selected firmware updates require online mode.");
        if (request.IsAutopilotEnabled && request.AutopilotProvisioningMode != AutopilotProvisioningMode.JsonProfile)
            blockers.Add("Selected Autopilot registration requires online mode.");
        if (request.DriverPackSelectionKind == DriverPackSelectionKind.OemCatalog && request.DriverPack is null)
            blockers.Add("The selected OEM driver package is unresolved.");
        const string path = DeployConfigurationService.DefaultConfigurationPath;
        DeploymentOfflineConfigurationSnapshot snapshot = DeploymentOfflineConfigurationSnapshot.Parse(
            await ReadConfigurationAsync(path, token).ConfigureAwait(false));
        return await EvaluateSnapshotAsync(snapshot, request.OperatingSystem,
            request.DriverPackSelectionKind == DriverPackSelectionKind.OemCatalog ? request.DriverPack : null,
            CacheBase(request.CacheRootPath), blockers, token, path, request).ConfigureAwait(false);
    }

    internal async Task<IReadOnlyList<string>> ValidateCurrentCustomizationAsync(DeploymentContext request,
        FoundryDeployConfigurationDocument document, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        List<string> blockers = [];
        if (document.Protection.IsEnabled && secretSession?.IsUnlocked != true)
            blockers.Add("Protected configuration must be unlocked before offline deployment.");
        if (request.Unattend is not null)
        {
            try
            {
                if (unattendContent is null) throw new InvalidDataException();
                using UnattendSnapshot validated = unattendContent.Read(request.Unattend, request.OperatingSystem.Architecture,
                    request.IsAutopilotEnabled, request.AutopilotProvisioningMode);
            }
            catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            { blockers.Add("The selected custom answer file is unavailable, locked, invalid, or incompatible."); }
        }
        if (request.IsAutopilotEnabled && request.AutopilotProvisioningMode == AutopilotProvisioningMode.JsonProfile)
        {
            byte[]? content = null;
            try
            {
                if (profiles is null || request.SelectedAutopilotProfile is null) throw new InvalidDataException();
                content = await profiles.ReadAsync(request.SelectedAutopilotProfile, token).ConfigureAwait(false);
                Foundry.Core.Services.Autopilot.AutopilotOfflineProfileValidator.Validate(content);
            }
            catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            { blockers.Add("The selected offline Autopilot profile is unavailable, locked, or invalid."); }
            finally { if (content is not null) CryptographicOperations.ZeroMemory(content); }
        }
        token.ThrowIfCancellationRequested();
        return blockers;
    }

    internal static string CacheBase(string runtimeRoot)
    {
        string path = runtimeRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.GetFileName(path).Equals("Runtime", StringComparison.OrdinalIgnoreCase) ? Path.GetDirectoryName(path)! : path;
    }

    internal static OfflineArtifactRequirement DriverRequirement(DriverPackCatalogItem driverPack, string cacheRoot) =>
        Requirement(ArtifactIntegrityPolicy.FromDriverPack(driverPack), cacheRoot,
            Path.Combine("DriverPacks", DeploymentStepExecutionContext.SanitizePathSegment(driverPack.Manufacturer)));

    private static OfflineArtifactRequirement Requirement(ArtifactIdentity identity, string root, string folder)
    {
        string parent = Path.Combine(root, "Cache", folder);
        return new(identity, [Path.Combine(parent, identity.CacheKey, identity.FileName), Path.Combine(parent, identity.FileName)]);
    }

    internal static async Task<byte[]> ReadConfigurationAsync(string path, CancellationToken token)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        if (stream.Length is <= 0 or > 4 * 1024 * 1024) throw new InvalidDataException("Configuration size is invalid.");
        byte[] bytes = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        if (await stream.ReadAsync(new byte[1], token).ConfigureAwait(false) != 0)
            throw new InvalidDataException("Configuration changed while being read.");
        return bytes;
    }

    private static async Task ValidateLocalCustomizationAsync(FoundryDeployConfigurationDocument document, string path, List<string> blockers, CancellationToken token)
    {
        if (document.Protection.IsEnabled) blockers.Add("Protected configuration must be unlocked and validated before offline readiness can be established.");
        if (document.Unattend.DefaultFileId is not null) blockers.Add("Custom answer-file selection must be resolved before offline readiness can be established.");
        if (!document.Autopilot.IsEnabled) return;
        if (document.Autopilot.ProvisioningMode != AutopilotProvisioningMode.JsonProfile)
        {
            blockers.Add("Autopilot registration requires online mode.");
            return;
        }
        string? folder = document.Autopilot.DefaultProfileFolderName;
        if (string.IsNullOrWhiteSpace(folder) || Path.GetFileName(folder) != folder)
        {
            blockers.Add("The offline Autopilot profile is unresolved.");
            return;
        }
        string profilePath = Path.Combine(Path.GetDirectoryName(path)!, "Autopilot", folder, "AutopilotConfigurationFile.json");
        try
        {
            byte[] content = await ReadConfigurationAsync(profilePath, token).ConfigureAwait(false);
            Foundry.Core.Services.Autopilot.AutopilotOfflineProfileValidator.Validate(content);
        }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException)
        { blockers.Add("The selected offline Autopilot profile is unavailable or invalid."); }
    }
}
