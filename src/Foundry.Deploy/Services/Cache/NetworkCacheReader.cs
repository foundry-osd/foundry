// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using Foundry.Core.Services.NetworkCache;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.Services.Cache;

/// <summary>A published download on the share and the SHA-256 that authenticates it when the catalog has no hash.</summary>
public sealed record NetworkCacheCandidate(string Path, string? SidecarSha256);

/// <summary>Finds downloads already published on the deployment's network cache share.</summary>
public interface INetworkCacheReader
{
    /// <summary>
    /// Returns the final share file for the artifact, or <see langword="null"/> when there is no session, the kind
    /// is not shared, the file is absent, or a hashless file has no sidecar. Never throws for share IO problems.
    /// </summary>
    Task<NetworkCacheCandidate?> FindAsync(string sourceUrl, string fileName, string? expectedHash, long? expectedSizeBytes, string? artifactKind, CancellationToken cancellationToken);
}

public sealed class NetworkCacheReader(INetworkCacheSessionAccessor accessor, ILogger<NetworkCacheReader> logger) : INetworkCacheReader
{
    public Task<NetworkCacheCandidate?> FindAsync(string sourceUrl, string fileName, string? expectedHash, long? expectedSizeBytes, string? artifactKind, CancellationToken cancellationToken)
    {
        NetworkCacheSession? session = accessor.Current;
        if (session?.SharePath is null || artifactKind is null || !NetworkCacheLayout.CacheableKinds.Contains(artifactKind))
        {
            return Task.FromResult<NetworkCacheCandidate?>(null);
        }

        string directory = NetworkCacheLayout.DownloadDirectory(session.SharePath, artifactKind, NetworkCacheLayout.DownloadKey(sourceUrl, expectedHash, expectedSizeBytes));
        string path = Path.Combine(directory, fileName);
        // UNC IO can block for the SMB timeout, so it never runs on the caller's thread.
        return Task.Run(() => Find(path, hashless: string.IsNullOrWhiteSpace(expectedHash)), cancellationToken);
    }

    private NetworkCacheCandidate? Find(string path, bool hashless)
    {
        try
        {
            if (!File.Exists(path)) return null;
            if (!hashless) return new(path, null);
            string sidecar = path + NetworkCacheLayout.HashSidecarSuffix;
            string? sha256 = File.Exists(sidecar) ? File.ReadAllText(sidecar).Trim() : null;
            if (string.IsNullOrWhiteSpace(sha256))
            {
                logger.LogWarning("Network cache file ignored: no hash sidecar. Path={Path}", path);
                return null;
            }

            return new(path, sha256);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("Network cache lookup failed. Path={Path}, Reason={Reason}", path, exception.GetType().Name);
            return null;
        }
    }
}
