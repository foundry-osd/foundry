// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.WinPe;
using Foundry.Deploy.Models;
using Foundry.Deploy.Services.Catalog;
using Foundry.Deploy.Services.Download;

namespace Foundry.Deploy.Services.Startup;

public sealed record DeploymentOfflineReadinessResult(bool CanContinue, Guid MediaId, string ConfigurationDigest,
    IReadOnlyList<string> CatalogRevisions, IReadOnlyList<string> BlockingReasons);

public sealed record OfflineArtifactRequirement(ArtifactIdentity Identity, IReadOnlyList<string> CandidatePaths);

public sealed record DeploymentOfflineReadinessRequest
{
    public required WinPeMediaManifest TrustedManifest { get; init; }
    public required Guid ExpectedMediaId { get; init; }
    public required string RuntimeIdentifier { get; init; }
    public required byte[] ConfigurationBytes { get; init; }
    public required string ExpectedConfigurationDigest { get; init; }
    public required DeploymentCatalogSnapshot Catalogs { get; init; }
    public OperatingSystemCatalogItem? OperatingSystem { get; init; }
    public IReadOnlyList<DriverPackCatalogItem> SelectedDriverPacks { get; init; } = [];
    public IReadOnlyList<OfflineArtifactRequirement> RequiredArtifacts { get; init; } = [];
    public IReadOnlyList<string> OnlineOnlyCapabilities { get; init; } = [];
    public bool SelectionsResolved { get; init; }
}
