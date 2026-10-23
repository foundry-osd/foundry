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
    /// Returns the final share file for the artifact, or <see langword="null"/> when there is no usable session, the kind
    /// is not shared, the artifact has neither hash nor size, the file is absent, or a hashless file has no sidecar.
    /// Never throws for share IO problems.
    /// </summary>
    Task<NetworkCacheCandidate?> FindAsync(string sourceUrl, string fileName, string? expectedHash, long? expectedSizeBytes, string? artifactKind, CancellationToken cancellationToken);

    /// <summary>Stops later lookups for this deployment after the share stalled during a copy.</summary>
    void MarkUnresponsive();
}

public sealed class NetworkCacheReader : INetworkCacheReader
{
    private readonly INetworkCacheSessionAccessor accessor;
    private readonly ILogger<NetworkCacheReader> logger;
    private readonly TimeSpan lookupTimeout;
    private readonly Func<string, bool> fileExists;

    public NetworkCacheReader(INetworkCacheSessionAccessor accessor, ILogger<NetworkCacheReader> logger)
        : this(accessor, logger, TimeSpan.FromSeconds(5), File.Exists)
    {
    }

    internal NetworkCacheReader(INetworkCacheSessionAccessor accessor, ILogger<NetworkCacheReader> logger, TimeSpan lookupTimeout, Func<string, bool> fileExists)
    {
        this.accessor = accessor;
        this.logger = logger;
        this.lookupTimeout = lookupTimeout;
        this.fileExists = fileExists;
    }

    public async Task<NetworkCacheCandidate?> FindAsync(string sourceUrl, string fileName, string? expectedHash, long? expectedSizeBytes, string? artifactKind, CancellationToken cancellationToken)
    {
        NetworkCacheSession? session = accessor.Current;
        bool hashless = string.IsNullOrWhiteSpace(expectedHash);
        // Without a hash or a size, a reused URL could serve new content that the share would never notice.
        if (session is not { IsAvailable: true, SharePath: not null } ||
            artifactKind is null || !NetworkCacheLayout.CacheableKinds.Contains(artifactKind) ||
            (hashless && expectedSizeBytes is not > 0))
        {
            return null;
        }

        string directory = NetworkCacheLayout.DownloadDirectory(session.SharePath, artifactKind, NetworkCacheLayout.DownloadKey(sourceUrl, expectedHash, expectedSizeBytes));
        string path = Path.Combine(directory, fileName);
        try
        {
            // UNC IO can block for the SMB timeout, so it runs on the thread pool and is abandoned after the lookup timeout.
            return await Task.Run(() => Find(path, hashless), cancellationToken)
                .WaitAsync(lookupTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            session.MarkUnresponsive();
            logger.LogWarning("Network cache share stopped answering; later downloads skip it. SharePath={SharePath}", session.SharePath);
            return null;
        }
    }

    public void MarkUnresponsive() => accessor.Current?.MarkUnresponsive();

    private NetworkCacheCandidate? Find(string path, bool hashless)
    {
        try
        {
            if (!fileExists(path)) return null;
            if (!hashless) return new(path, null);
            string sidecar = path + NetworkCacheLayout.HashSidecarSuffix;
            string? sha256 = fileExists(sidecar) ? File.ReadAllText(sidecar).Trim() : null;
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
