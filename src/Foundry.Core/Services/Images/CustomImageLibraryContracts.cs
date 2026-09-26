// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Images;

/// <summary>Reads every native image index. Callers own read leases for the duration of inspection.</summary>
public interface ICustomImageMetadataReader
{
    Task<IReadOnlyList<CustomImageIndex>> ReadAsync(string imagePath, CancellationToken cancellationToken = default);
}

/// <summary>Requests a local WIM or the selected installation image from an ISO.</summary>
public sealed record CustomImageImportRequest(string SourcePath, string DisplayName, string? IsoImagePath = null);

/// <summary>Requests an explicit choice when an ISO has several supported installation containers.</summary>
public sealed class CustomImageSourceChoiceRequiredException(IReadOnlyList<string> candidates)
    : IOException("Select the installation image to import from this ISO.")
{
    public IReadOnlyList<string> Candidates { get; } = candidates;
}

/// <summary>Reports streamed bytes and an operation stage without exposing source data to telemetry.</summary>
public sealed record CustomImageImportProgress(string Stage, long CompletedBytes, long TotalBytes);

/// <summary>Holds a deny-write/delete handle through verification and all subsequent image consumption.</summary>
public sealed class CustomImageSourceLease : IDisposable, IAsyncDisposable
{
    private readonly FileStream image;
    internal CustomImageSourceLease(CustomImageReference reference, string imagePath, FileStream image)
    {
        Reference = reference;
        ImagePath = imagePath;
        this.image = image;
    }

    public CustomImageReference Reference { get; }
    public string ImagePath { get; }
    public void Dispose() => image.Dispose();
    public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
}
