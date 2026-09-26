// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.PreOobe;

/// <summary>Pins companion runtime assets inside an authenticated Deploy release payload.</summary>
public sealed record PreOobeRuntimeDescriptor
{
    public int SchemaVersion { get; init; } = 1;
    public string ReleaseTag { get; init; } = string.Empty;
    public int ContractVersion { get; init; } = 1;
    public IReadOnlyList<PreOobeRuntimeAsset> Assets { get; init; } = [];
}

/// <summary>Bounds archive transfer and extraction before allowing a verified companion to execute.</summary>
public sealed record PreOobeRuntimeAsset
{
    public string RuntimeIdentifier { get; init; } = string.Empty;
    public string AssetName { get; init; } = string.Empty;
    public string ArchiveSha256 { get; init; } = string.Empty;
    public long ArchiveLength { get; init; }
    public long ExpandedLength { get; init; }
    public int EntryCount { get; init; }
}
