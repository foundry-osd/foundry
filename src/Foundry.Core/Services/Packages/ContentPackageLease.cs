// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Packages;
using Foundry.Core.Models.PreOobe;

namespace Foundry.Core.Services.Packages;

/// <summary>Holds verified read handles until media publication or target staging has finished.</summary>
public sealed class ContentPackageLease : IDisposable, IAsyncDisposable
{
    private readonly IReadOnlyList<FileStream> handles;
    private bool disposed;

    internal ContentPackageLease(ContentPackageReference reference, ContentPackageManifest manifest, string directory,
        IReadOnlyList<ContentPackageSourceFile> files, IReadOnlyList<FileStream> handles)
    {
        Reference = reference;
        Manifest = manifest;
        ContentDirectoryPath = directory;
        Files = files;
        this.handles = handles;
    }

    public ContentPackageReference Reference { get; }
    public ContentPackageManifest Manifest { get; }
    public string ContentDirectoryPath { get; }
    public IReadOnlyList<ContentPackageSourceFile> Files { get; }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (FileStream handle in handles) handle.Dispose();
    }

    public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
}

/// <summary>Maps one held source to the original package-relative destination.</summary>
public sealed record ContentPackageSourceFile(string SourcePath, string RelativePath, long Length, string Sha256);
