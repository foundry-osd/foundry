// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using Foundry.Core.Models.Configuration;

namespace Foundry.Deploy.Models;

/// <summary>A media-bound image candidate; manual files never inherit managed defaults by filename.</summary>
public sealed record CustomImageAsset
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string ImagePath { get; init; }
    public required string VolumeRoot { get; init; }
    public bool IsOptical { get; init; }
    public long? ExpectedLength { get; init; }
    public string? ExpectedHash { get; init; }
    public string? ObservedHash { get; init; }
    public long? ObservedLength { get; init; }
    public bool IsManaged => ExpectedHash is not null;
    public string DisplayLabel => $"{DisplayName} ({VolumeRoot})";
}

/// <summary>Retains the exact chosen WIM index and actual metadata without catalog edition resolution.</summary>
public sealed record CustomImageSelection : OperatingSystemMetadata
{
    public CustomImageSelection(CustomImageAsset asset, CustomImageIndex index)
    {
        Asset = asset;
        Index = index;
        SourceId = asset.Id;
        FileName = Path.GetFileName(asset.ImagePath);
        SizeBytes = asset.ExpectedLength ?? 0;
        Architecture = index.Architecture;
        Edition = index.EditionId;
        Build = index.Version ?? string.Empty;
        BuildMajor = index.Build ?? 0;
        LanguageCode = index.DefaultLanguage ?? string.Empty;
        Language = string.Join(", ", index.Languages);
        ClientType = index.ProductType;
        if (index.ProductType.Equals("WinNT", StringComparison.OrdinalIgnoreCase) &&
            OperatingSystemSelectionCatalog.SupportedReleases.FirstOrDefault(release => release.Build == index.Build) is { } knownRelease)
        {
            WindowsRelease = OperatingSystemSupportMatrix.SupportedWindowsRelease;
            ReleaseId = knownRelease.Id;
        }
    }

    public CustomImageAsset Asset { get; }
    public CustomImageIndex Index { get; }
    public override string DisplayLabel => $"{Asset.DisplayName} | {Index.Index}: {Index.Name} | {Architecture} | {Edition} | {Build}";
}
