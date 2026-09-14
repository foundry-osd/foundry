// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Identifies a catalog source and the edition to export for boot dependency preparation.
/// </summary>
internal sealed record WindowsSourceCandidate
{
    /// <summary>
    /// Gets the edition to resolve in the source image; Pro is preferred and Enterprise is the fallback.
    /// </summary>
    public required string RequestedEdition { get; init; }
    /// <summary>
    /// Gets the catalog metadata used to select and cache the source package.
    /// </summary>
    public required WindowsSourceCatalogItem Source { get; init; }
}

/// <summary>
/// Holds operating system catalog metadata used for build, architecture, language, and package selection.
/// </summary>
internal sealed record WindowsSourceCatalogItem
{
    public required string WindowsRelease { get; init; }
    public required string ReleaseId { get; init; }
    public required int BuildMajor { get; init; }
    public required int BuildUbr { get; init; }
    public required string Architecture { get; init; }
    public required string LanguageCode { get; init; }
    public required string ClientType { get; init; }
    public required string LicenseChannel { get; init; }
    public required string FileName { get; init; }
    public required string Url { get; init; }
    public required string Sha256 { get; init; }
}
