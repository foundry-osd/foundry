// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

internal sealed record WinReSourceCandidate
{
    public required string RequestedEdition { get; init; }
    public required WinReCatalogItem Source { get; init; }
}

internal sealed record WinReCatalogItem
{
    public required string WindowsRelease { get; init; }
    public required string ReleaseId { get; init; }
    public required int BuildMajor { get; init; }
    public required int BuildUbr { get; init; }
    public required string Architecture { get; init; }
    public required string LanguageCode { get; init; }
    public required string Edition { get; init; }
    public required string ClientType { get; init; }
    public required string LicenseChannel { get; init; }
    public required string FileName { get; init; }
    public required string Url { get; init; }
    public required string Sha256 { get; init; }
}
