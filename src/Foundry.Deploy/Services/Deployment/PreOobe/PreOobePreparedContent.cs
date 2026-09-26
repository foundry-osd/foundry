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
    public required PreOobeRuntimeAsset RuntimeAsset { get; init; }
    public string RuntimeArchivePath { get; set; } = string.Empty;
    public string? TemporaryRuntimeDirectory { get; set; }
    public List<PreOobePreparedPackage> Packages { get; } = [];
    public List<(string Path, FileStream Stream)> Files { get; } = [];
    public long TargetBytes => checked(RuntimeAsset.ExpandedLength + Packages.Sum(package => package.Manifest.Files.Sum(file => file.Length)));

    public void Dispose()
    {
        foreach (var file in Files) file.Stream.Dispose();
        if (TemporaryRuntimeDirectory is not null)
        {
            string path = Path.GetFullPath(TemporaryRuntimeDirectory);
            string root = Path.GetFullPath(@"X:\Foundry\Temp\PostInstall") + Path.DirectorySeparatorChar;
            if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase) && Directory.Exists(path))
            {
                try { Foundry.Core.Services.Packages.PreOobePackagePathPolicy.ValidateNoReparsePoints(path); Directory.Delete(path, recursive: true); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            }
        }
    }
}

internal sealed record PreOobePreparedPackage(string ContentHash, string SourceRoot, PreOobePackageManifest Manifest);
