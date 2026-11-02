// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Packages;

namespace Foundry.Core.Models.PreOobe;

/// <summary>Describes the execution contract and files of an independently published PostInstall runtime.</summary>
public sealed record PostInstallRuntimeManifest
{
    public const string FileName = "foundry.postinstall.json";
    public const string ExecutableEnvironmentVariable = "FOUNDRY_POSTINSTALL_PATH";
    public int SchemaVersion { get; init; } = 1;
    public int ContractVersion { get; init; } = 1;
    public string RuntimeIdentifier { get; init; } = string.Empty;

    /// <summary>
    /// Gets the GitHub release tag that published this runtime, for example <c>v26.11.2.1</c>, or <see langword="null"/> for
    /// runtimes built locally or published before release tags were recorded.
    /// </summary>
    /// <remarks>
    /// Foundry.Deploy accepts only the runtime of its own release, so a boot media runtime from another release is replaced
    /// before the target disk is erased. Older readers ignore this member, so it does not change <see cref="ContractVersion"/>.
    /// </remarks>
    public string? ReleaseTag { get; init; }

    public IReadOnlyList<ContentPackageFile> Files { get; init; } = [];
}
