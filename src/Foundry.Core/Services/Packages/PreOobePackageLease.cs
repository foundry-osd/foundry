// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.PreOobe;

namespace Foundry.Core.Services.Packages;

/// <summary>Holds verified read handles until media publication or target staging has finished.</summary>
public sealed class PreOobePackageLease : IDisposable, IAsyncDisposable
{
    private readonly IReadOnlyList<FileStream> handles;
    private bool disposed;

    internal PreOobePackageLease(PreOobePackageReference reference, PreOobePackageManifest manifest, string directory,
        IReadOnlyList<PreOobePackageSourceFile> files, IReadOnlyList<FileStream> handles)
    {
        Reference = reference;
        Manifest = manifest;
        ContentDirectoryPath = directory;
        Files = files;
        this.handles = handles;
    }

    public PreOobePackageReference Reference { get; }
    public PreOobePackageManifest Manifest { get; }
    public string ContentDirectoryPath { get; }
    public IReadOnlyList<PreOobePackageSourceFile> Files { get; }
    public void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (FileStream handle in handles) handle.Dispose();
    }

    public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
}

/// <summary>Maps one held source to the original package-relative destination.</summary>
public sealed record PreOobePackageSourceFile(string SourcePath, string RelativePath, long Length, string Sha256);
