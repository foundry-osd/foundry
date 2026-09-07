// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Security.Cryptography;
using Foundry.Core.Services.Catalog;
using Foundry.Core.Services.WinPe;
using Foundry.Deploy.Models;
using Foundry.Deploy.Services.Catalog;
using Foundry.Deploy.Services.Download;
using Foundry.Deploy.Services.Deployment;

namespace Foundry.Deploy.Services.Startup;

public sealed class DeploymentOfflineReadinessService(IArtifactDownloadService downloads)
{
    public async Task<DeploymentOfflineReadinessResult> EvaluateAsync(DeploymentOfflineReadinessRequest request, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        token.ThrowIfCancellationRequested();
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
        budget.CancelAfter(TimeSpan.FromMinutes(10));
        byte[] configuration = request.ConfigurationBytes.ToArray();
        string digest = Convert.ToHexString(SHA256.HashData(configuration));
        var requirements = request.RequiredArtifacts.Select(item => item with { CandidatePaths = item.CandidatePaths.ToArray() }).ToArray();
        var selectedDrivers = request.SelectedDriverPacks.ToArray();
        List<string> failures = [];
        string[] revisions = new[] { request.Catalogs.OperatingSystemSnapshot?.Revision, request.Catalogs.DriverPackSnapshot?.Revision }
            .OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
        DeploymentOfflineReadinessResult Result() => new(failures.Count == 0, request.TrustedManifest.MediaId, digest, revisions, failures.ToArray());
        try
        {
            WinPeMediaManifestStore.Validate(request.TrustedManifest, request.RuntimeIdentifier, request.ExpectedMediaId);
            if (configuration.Length is < 1 or > 32 * 1024 * 1024 || !string.Equals(digest, request.ExpectedConfigurationDigest, StringComparison.OrdinalIgnoreCase))
                failures.Add("The selected configuration does not match the current readiness request.");
            if (!request.SelectionsResolved) failures.Add("One or more selected customization or protected content requirements are unresolved.");
            if (request.OnlineOnlyCapabilities.Count > 0) failures.Add("One or more selected operations require a network connection before deployment.");
            var osSnapshot = request.Catalogs.OperatingSystemSnapshot;
            if (!Pinned(osSnapshot, VerifiedCatalogSources.OperatingSystems, request.TrustedManifest))
                failures.Add("The operating system catalog is not a verified local snapshot for this media.");
            OperatingSystemCatalogItem? os = request.OperatingSystem;
            if (os is null || osSnapshot is null || !osSnapshot.Items.Contains(os) || os.CatalogRevision != osSnapshot.Revision ||
                "win-" + os.Architecture != request.RuntimeIdentifier)
                failures.Add("Select an operating system from this media's verified catalog for the current architecture.");
            else
            {
                DeploymentPreflightService.ValidateSelection(os);
                RequireIdentity(ArtifactIntegrityPolicy.FromOperatingSystem(os), requirements, failures);
            }

            if (selectedDrivers.Length > 0)
            {
                var driverSnapshot = request.Catalogs.DriverPackSnapshot;
                if (!Pinned(driverSnapshot, VerifiedCatalogSources.DriverPacks, request.TrustedManifest))
                    failures.Add("The selected driver or firmware catalog is not verified for this media.");
                foreach (DriverPackCatalogItem driver in selectedDrivers)
                {
                    if (driverSnapshot is null || !driverSnapshot.Items.Contains(driver) || driver.CatalogRevision != driverSnapshot.Revision)
                        failures.Add("A selected driver or firmware package is not in the verified local catalog.");
                    else RequireIdentity(ArtifactIntegrityPolicy.FromDriverPack(driver), requirements, failures);
                }
            }
            if (requirements.Length > 10000) failures.Add("The selected artifact set exceeds the readiness limit.");
            if (failures.Count > 0) return Result();
            foreach (OfflineArtifactRequirement requirement in requirements)
            {
                budget.Token.ThrowIfCancellationRequested();
                ArtifactIntegrityPolicy.Validate(requirement.Identity);
                if (requirement.Identity.Integrity.Digest is null || requirement.CandidatePaths.Count is < 1 or > 16)
                {
                    failures.Add("A selected artifact has no independently verifiable local identity or location.");
                    continue;
                }
                bool found = false;
                foreach (string path in requirement.CandidatePaths)
                {
                    try
                    {
                        RejectReparseAncestors(path);
                        if (await downloads.TryUseCachedAsync(requirement.Identity, path, budget.Token).ConfigureAwait(false) is not null)
                        { found = true; break; }
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
                }
                if (!found) failures.Add("A selected artifact is missing, inaccessible, or corrupt in local storage.");
            }
            budget.Token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested && budget.IsCancellationRequested)
        { failures.Add("Local artifact verification exceeded its ten-minute deadline."); }
        catch (Exception error) when (error is InvalidDataException or ArgumentException or FormatException)
        { failures.Add("The selected media, catalog, or artifact identity is invalid."); }
        return Result();
    }

    private static void RequireIdentity(ArtifactIdentity expected, IReadOnlyList<OfflineArtifactRequirement> requirements, List<string> failures)
    {
        if (requirements.Count(item => item.Identity == expected) != 1)
            failures.Add("A selected operating system, driver, or firmware artifact is missing or ambiguous in the readiness request.");
    }

    private static void RejectReparseAncestors(string path)
    {
        for (string? current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Offline payload storage must not traverse a reparse point.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    private static bool Pinned<T>(CatalogSnapshot<T>? snapshot, string id, WinPeMediaManifest manifest)
    {
        if (snapshot is null || !snapshot.IsOffline) return false;
        WinPeCatalogSnapshot? descriptor = manifest.CatalogSnapshots.SingleOrDefault(item => item.Id == id);
        return descriptor is not null && descriptor.Revision == snapshot.Revision && descriptor.SourceUri.AbsoluteUri == snapshot.SourceUri.AbsoluteUri &&
            descriptor.RetrievedUtc == snapshot.RetrievedUtc;
    }
}
