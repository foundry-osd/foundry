// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using Foundry.Core.Models.PreOobe;

namespace Foundry.Core.Services.WinPe;

/// <summary>Retains immutable package sources and runner archives until external media publication completes.</summary>
public sealed class WinPePreOobeMediaLease : IDisposable
{
    private readonly IReadOnlyList<IDisposable> leases;
    private bool disposed;

    internal WinPePreOobeMediaLease(PreOobeMediaManifest manifest, byte[] bytes, IReadOnlyList<WinPePreOobeMediaFile> files,
        IReadOnlyList<string> directories, IReadOnlyList<IDisposable> leases)
    {
        Manifest = manifest;
        ManifestBytes = bytes;
        Files = files;
        Directories = directories;
        this.leases = leases;
        ManifestHash = Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    public PreOobeMediaManifest Manifest { get; }
    public string ManifestId => Manifest.Id;
    public string ManifestHash { get; }
    public string ManifestRelativePath => $"Cache/PreOobe/manifests/{ManifestId}.json";
    public byte[] ManifestBytes { get; }
    public IReadOnlyList<WinPePreOobeMediaFile> Files { get; }
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
public sealed record WinPePreOobeMediaFile(string SourcePath, string RelativePath, long Length, string ContentHash);
