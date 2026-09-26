// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using Foundry.Core.Models.Images;

namespace Foundry.Core.Services.WinPe;

/// <summary>Retains immutable image inputs and the exact manifest bytes bound into one boot image.</summary>
public sealed class WinPeCustomImageMediaLease : IDisposable
{
    private readonly IReadOnlyList<IDisposable> leases;
    private bool disposed;

    internal WinPeCustomImageMediaLease(string manifestId, byte[] manifestBytes,
        IReadOnlyList<WinPeCustomImageMediaFile> files, IReadOnlyList<IDisposable> leases)
    {
        ManifestId = manifestId;
        ManifestBytes = manifestBytes;
        ManifestHash = Convert.ToHexString(SHA256.HashData(manifestBytes));
        Files = files;
        this.leases = leases;
    }

    /// <summary>Identifies this immutable media configuration.</summary>
    public string ManifestId { get; }
    /// <summary>Authenticates the exact serialized manifest referenced by the boot configuration.</summary>
    public string ManifestHash { get; }
    /// <summary>Locates the manifest relative to the ISO or USB data-volume root.</summary>
    public string ManifestRelativePath => Path.Combine(CustomImageMediaPaths.RelativeRoot, "manifests", ManifestId + ".json");
    /// <summary>Gets all retained image inputs, with content-addressed destination paths.</summary>
    public IReadOnlyList<WinPeCustomImageMediaFile> Files { get; }
    /// <summary>Gets the source bytes required on a new data volume, excluding filesystem reserves.</summary>
    public long TotalBytes => Files.Aggregate((long)ManifestBytes.Length, (total, file) => checked(total + file.Length));
    internal byte[] ManifestBytes { get; }
    internal void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

    /// <summary>Releases retained input leases after all requested media outputs have completed.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (IDisposable lease in leases.Reverse()) lease.Dispose();
    }
}

/// <summary>Describes a verified source file and its immutable destination on deployment media.</summary>
public sealed record WinPeCustomImageMediaFile(string SourcePath, string RelativePath, long Length, string ContentHash);
