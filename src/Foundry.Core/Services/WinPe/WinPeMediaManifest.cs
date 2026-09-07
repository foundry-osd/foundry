// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Media;

namespace Foundry.Core.Services.WinPe;

/// <summary>Projects authenticated runtime identities without authoring filesystem paths.</summary>
public sealed record WinPeRuntimeApplicationManifest(
    string ApplicationName, string RuntimeIdentifier, WinPeProvisioningSource Source,
    string? ReleaseTag, string? ArchiveSha256, IReadOnlyList<WinPeRuntimeFile> Files);

/// <summary>Pins the exact authenticated catalog bytes embedded in the boot image.</summary>
public sealed record WinPeCatalogSnapshot(
    string Id, string RelativePath, string Sha256, string Revision,
    Uri SourceUri, DateTimeOffset RetrievedUtc, long Length);

/// <summary>The authored boot image's trust root for runtime files and offline catalogs.</summary>
public sealed record WinPeMediaManifest(
    int Version, Guid MediaId, string RuntimeIdentifier, MediaOperationTarget Target,
    WinPeUsbDiskIdentity? IntendedUsbIdentity,
    IReadOnlyList<WinPeRuntimeApplicationManifest> Applications,
    IReadOnlyList<WinPeCatalogSnapshot> CatalogSnapshots);
