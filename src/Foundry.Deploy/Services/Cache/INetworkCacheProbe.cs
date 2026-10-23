// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using Foundry.Core.Services.NetworkCache;

namespace Foundry.Deploy.Services.Cache;

/// <summary>Checks a connected share before Deploy relies on it; callers apply their own timeout.</summary>
public interface INetworkCacheProbe
{
    /// <summary>Tells whether <paramref name="contentRoot"/> exists or can be created.</summary>
    Task<bool> CanReadAsync(string contentRoot, CancellationToken cancellationToken);

    /// <summary>Creates and deletes a session-named probe file under the downloads folder.</summary>
    Task<bool> CanWriteAsync(string contentRoot, string sessionId, CancellationToken cancellationToken);
}

/// <summary>File system probe run on the thread pool, because UNC IO can block for the SMB timeout.</summary>
public sealed class FileSystemNetworkCacheProbe : INetworkCacheProbe
{
    public Task<bool> CanReadAsync(string contentRoot, CancellationToken cancellationToken) =>
        Task.Run(() => Directory.Exists(contentRoot) || Directory.Exists(Path.GetDirectoryName(contentRoot)), cancellationToken);

    public Task<bool> CanWriteAsync(string contentRoot, string sessionId, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            string downloads = Path.Combine(contentRoot, NetworkCacheLayout.DownloadsFolderName);
            string probe = Path.Combine(downloads, $".probe-{sessionId}");
            try
            {
                Directory.CreateDirectory(downloads);
                File.WriteAllBytes(probe, []);
                File.Delete(probe);
                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }, cancellationToken);
}
