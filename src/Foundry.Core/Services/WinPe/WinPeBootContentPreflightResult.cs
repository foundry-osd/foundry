// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

/// <summary>Owns the measured BOOT source leases and the resulting minimum layout capacities.</summary>
internal sealed class WinPeBootContentPreflightResult : IDisposable
{
    internal List<FileStream> SourceHandles { get; } = [];
    internal List<WinPeMeasuredBootFile> Files { get; } = [];
    public required ulong BootFileBytes { get; init; }
    public required ulong LargestBootFileBytes { get; init; }
    public required ulong RuntimeCacheBytes { get; init; }
    public required ulong BootPartitionSizeBytes { get; init; }
    public required ulong RequiredBootBytes { get; init; }
    public required ulong RequiredCacheFreeBytes { get; init; }

    /// <summary>Releases finalized sources after copying or abandoning the USB operation.</summary>
    public void Dispose()
    {
        foreach (FileStream stream in SourceHandles) { stream.Dispose(); }
        SourceHandles.Clear();
        Files.Clear();
    }
}

/// <summary>Pairs a measured relative destination with the exact protected source stream.</summary>
internal sealed record WinPeMeasuredBootFile(string RelativePath, FileStream Source);
