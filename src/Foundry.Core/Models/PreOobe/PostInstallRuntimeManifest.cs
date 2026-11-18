// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Packages;

namespace Foundry.Core.Models.PreOobe;

/// <summary>Describes the execution contract and files of an independently published PostInstall runtime.</summary>
public sealed record PostInstallRuntimeManifest
{
    public const string FileName = "foundry.postinstall.json";

    /// <summary>Names the single-file PostInstall executable, listed in <see cref="Files"/>.</summary>
    public const string ExecutableFileName = "Foundry.PostInstall.exe";

    /// <summary>Names the script Windows setup runs to start PostInstall, listed in <see cref="Files"/>.</summary>
    public const string LaunchScriptFileName = "Launch.cmd";
    public int SchemaVersion { get; init; } = 1;
    public int ContractVersion { get; init; } = 1;
    public string RuntimeIdentifier { get; init; } = string.Empty;

    /// <summary>
    /// Gets the GitHub release tag that published this runtime, for example <c>v26.11.2.1</c>, or <see langword="null"/> for
    /// runtimes built locally or published before release tags were recorded.
    /// </summary>
    /// <remarks>
    /// Informational only: the runtime ships in the boot image built with the Foundry.WinPE of the same Foundry OSD, so
    /// Foundry.Deploy never compares it. Older readers ignore this member, so it does not change <see cref="ContractVersion"/>.
    /// </remarks>
    public string? ReleaseTag { get; init; }

    public IReadOnlyList<ContentPackageFile> Files { get; init; } = [];
}
