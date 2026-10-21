// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using Foundry.Core.Models.Packages;
using Foundry.Core.Services.Packages;

namespace Foundry.Core.Services.WinPe;

/// <summary>Retains immutable content package sources until external media publication completes.</summary>
public sealed class WinPeContentPackageMediaLease : IDisposable
{
    private readonly IReadOnlyList<IDisposable> leases;
    private bool disposed;

    internal WinPeContentPackageMediaLease(ContentPackageKind kind, ContentPackageMediaManifest manifest, byte[] bytes,
        IReadOnlyList<WinPeContentPackageMediaFile> files,
        IReadOnlyList<string> directories, IReadOnlyList<IDisposable> leases)
    {
        Kind = kind;
        Manifest = manifest;
        ManifestBytes = bytes;
        Files = files;
        Directories = directories;
        this.leases = leases;
        ManifestHash = Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    public ContentPackageKind Kind { get; }
    public ContentPackageMediaManifest Manifest { get; }
    public string ManifestId => Manifest.Id;
    public string ManifestHash { get; }
    public string ManifestRelativePath => $"{Kind.MediaRoot}/manifests/{ManifestId}.json";
    public byte[] ManifestBytes { get; }
    public IReadOnlyList<WinPeContentPackageMediaFile> Files { get; }
    public IReadOnlyList<string> Directories { get; }
    public long TotalBytes => Files.Aggregate((long)ManifestBytes.Length, (total, file) => checked(total + file.Length));
    public void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (IDisposable lease in leases.Reverse()) lease.Dispose();
    }
}

/// <summary>Maps a verified held source to its content-addressed media destination.</summary>
public sealed record WinPeContentPackageMediaFile(string SourcePath, string RelativePath, long Length, string ContentHash);
