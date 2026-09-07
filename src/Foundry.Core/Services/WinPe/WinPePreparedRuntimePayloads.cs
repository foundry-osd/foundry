// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

/// <summary>Identifies the exact prepared bytes of a runtime file, including loader dependencies.</summary>
public sealed record WinPeRuntimeFile(string RelativePath, long Length, string Sha256);

/// <summary>Describes one prepared application and the acquisition identity captured before placement.</summary>
public sealed record WinPePreparedRuntimeApplication(
    string ApplicationName,
    string RuntimeIdentifier,
    string DirectoryPath,
    WinPeProvisioningSource Source,
    string? ReleaseTag,
    string? ArchiveSha256,
    IReadOnlyList<WinPeRuntimeFile> Files);

/// <summary>
/// Owns prepared runtime files until all image and cache placements finish. Disposal releases read
/// leases and removes only the preparation directory created by the service, never caller archives.
/// </summary>
public sealed class WinPePreparedRuntimePayloads(Guid mediaId, IReadOnlyList<WinPePreparedRuntimeApplication> applications) : IDisposable
{
    private readonly List<FileStream> _readLeases = [];
    private string? _ownedWorkspacePath;

    /// <summary>Gets the identity of this preparation operation.</summary>
    public Guid MediaId { get; } = mediaId;

    /// <summary>Gets the immutable snapshot used for both image placement and cache sizing.</summary>
    public IReadOnlyList<WinPePreparedRuntimeApplication> Applications { get; } = Array.AsReadOnly(
        applications.Select(application => application with { Files = Array.AsReadOnly(application.Files.ToArray()) }).ToArray());

    internal bool IsDisposed { get; private set; }

    internal void TakeOwnership(string workspacePath, IEnumerable<FileStream> readLeases)
    {
        _ownedWorkspacePath = workspacePath;
        _readLeases.AddRange(readLeases);
    }

    /// <summary>Releases file protection after the last placement and cleans the owned staging directory.</summary>
    public void Dispose()
    {
        if (IsDisposed)
        {
            return;
        }

        IsDisposed = true;
        foreach (FileStream lease in _readLeases)
        {
            lease.Dispose();
        }

        _readLeases.Clear();
        if (_ownedWorkspacePath is not null)
        {
            try
            {
                Directory.Delete(_ownedWorkspacePath, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Cleanup must not replace the primary media operation result.
            }
        }
    }
}
