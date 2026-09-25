// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Images;

namespace Foundry.Core.Services.Images;

/// <summary>Reads every native image index. Callers own read leases for the duration of inspection.</summary>
public interface ICustomImageMetadataReader
{
    Task<IReadOnlyList<CustomImageIndex>> ReadAsync(string imagePath, CancellationToken cancellationToken = default);
}

/// <summary>Requests a local WIM or ISO import; optional sources are retained from ISO sources/sxs when present.</summary>
public sealed record CustomImageImportRequest(string SourcePath, string DisplayName, bool IncludeOptionalFeatureSources = true, string? IsoImagePath = null);

/// <summary>Requests an explicit choice when an ISO has several supported installation containers.</summary>
public sealed class CustomImageSourceChoiceRequiredException(IReadOnlyList<string> candidates)
    : IOException("Select the installation image to import from this ISO.")
{
    public IReadOnlyList<string> Candidates { get; } = candidates;
}

/// <summary>Reports streamed bytes and an operation stage without exposing source data to telemetry.</summary>
public sealed record CustomImageImportProgress(string Stage, long CompletedBytes, long TotalBytes);

/// <summary>Holds deny-write/delete handles through verification and all subsequent content consumption.</summary>
public sealed class CustomImageSourceLease : IDisposable, IAsyncDisposable
{
    private readonly IReadOnlyList<FileStream> handles;
    internal CustomImageSourceLease(CustomImageReference reference, string imagePath, string? sourceDirectoryPath,
        IReadOnlyList<CustomImageSourceFile> sourceFiles, IReadOnlyList<FileStream> handles)
    {
        Reference = reference;
        ImagePath = imagePath;
        SourceDirectoryPath = sourceDirectoryPath;
        SourceFiles = sourceFiles;
        this.handles = handles;
    }

    public CustomImageReference Reference { get; }
    public string ImagePath { get; }
    public string? SourceDirectoryPath { get; }
    public IReadOnlyList<CustomImageSourceFile> SourceFiles { get; }
    public void Dispose() { foreach (FileStream handle in handles) handle.Dispose(); }
    public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
}
