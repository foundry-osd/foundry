// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.PreOobe;

/// <summary>Describes the execution contract and files of an independently published PostInstall runtime.</summary>
public sealed record PostInstallRuntimeManifest
{
    public const string FileName = "foundry.postinstall.json";
    public const string ExecutableEnvironmentVariable = "FOUNDRY_POSTINSTALL_PATH";
    public int SchemaVersion { get; init; } = 1;
    public int ContractVersion { get; init; } = 1;
    public string RuntimeIdentifier { get; init; } = string.Empty;
    public IReadOnlyList<PreOobePackageFile> Files { get; init; } = [];
}
