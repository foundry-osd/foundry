// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Describes one user-facing Windows optional feature.
/// </summary>
public sealed record WindowsOptionalFeatureCatalogEntry
{
    public required string Id { get; init; }

    public required string FeatureName { get; init; }

    public required string DisplayNameResourceKey { get; init; }

    public required string CategoryResourceKey { get; init; }

    public string? ParentId { get; init; }

    public int SortOrder { get; init; }

    public IReadOnlyList<string> KnownSupportedEditionIds { get; init; } = [];

    public IReadOnlyList<string> KnownUnsupportedEditionIds { get; init; } = [];

    public IReadOnlyList<WinPeArchitecture> SupportedArchitectures { get; init; } = [];

    public int? MinimumBuild { get; init; }

    public int? MaximumBuildExclusive { get; init; }

    public bool RequiresSetupMediaSxs { get; init; }

    public string? WarningResourceKey { get; init; }
}
