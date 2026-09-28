// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using Foundry.Core.Models.PreOobe;

namespace Foundry.Deploy.Services.Deployment.PreOobe;

/// <summary>Owns verified source handles until target publication; packages are never evacuated into WinPE RAM.</summary>
internal sealed class PreOobePreparedContent : IDisposable
{
    public required string RuntimeIdentifier { get; init; }
    public required string RuntimeDirectory { get; init; }
    public required PostInstallRuntimeManifest RuntimeManifest { get; init; }
    public List<PreOobePreparedPackage> Packages { get; } = [];
    public List<(string Path, FileStream Stream)> Files { get; } = [];
    public long TargetBytes => checked(RuntimeManifest.Files.Sum(file => file.Length) + Packages.Sum(package => package.Manifest.Files.Sum(file => file.Length)));

    public void Dispose()
    {
        foreach (var file in Files) file.Stream.Dispose();
    }
}

internal sealed record PreOobePreparedPackage(string ContentHash, string SourceRoot, PreOobePackageManifest Manifest);
