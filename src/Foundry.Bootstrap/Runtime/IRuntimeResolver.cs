// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Bootstrap.Runtime;

/// <summary>Resolves Connect, Deploy, and PostInstall payloads without launching them, and locates the debug-only AutopilotInteractive archive.</summary>
internal interface IRuntimeResolver
{
    /// <summary>
    /// Resolves an executable from an explicit archive override, else from the release of the boot media: an embedded runtime
    /// runs from the media and is repaired from that release when missing or damaged; a runtime the media does not embed is
    /// downloaded from that release (or reused from the USB cache when it matches). <paramref name="skipReleaseLookup"/>
    /// (debug applications) allows the media payload only.
    /// </summary>
    Task<string> ResolveAsync(string applicationName, bool skipReleaseLookup, CancellationToken cancellationToken);

    /// <summary>
    /// Locates the original archive of a payload that exists only on debug media, with the SHA256 the boot-owned trust manifest
    /// records for it. Never extracts, downloads or caches the archive; the consumer authenticates it against the returned hash.
    /// </summary>
    /// <returns>The archive path and hash, or null when the trust manifest has no entry or the archive is not on the media.</returns>
    (string ArchivePath, string Sha256)? TryGetDebugArchive(string applicationName);
}

/// <summary>Reports measured payload transfer progress; an absent total means the size is unknown.</summary>
internal sealed record RuntimeDownloadProgress(string ApplicationName, long BytesReceived, long? TotalBytes, RuntimeProgressPhase Phase = RuntimeProgressPhase.Download);

/// <summary>Identifies the measured payload operation.</summary>
internal enum RuntimeProgressPhase { Download, Verification, Extraction }
