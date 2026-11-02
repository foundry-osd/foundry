// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Foundry.Core.Services.Runtime;
using Foundry.Utilities.IO;
using Serilog;

namespace Foundry.Bootstrap.Runtime;

/// <summary>
/// Resolves runtimes from the release of the boot media only. Runtimes embedded in the media (a trust manifest hash exists)
/// run from the media and are repaired from that release when missing or damaged. Runtimes the media does not embed are
/// downloaded from that release at every start; USB media keep the verified archive in their cache partition and reuse it
/// while it matches the release asset digest.
/// </summary>
/// <remarks>
/// <paramref name="releaseVersion"/> identifies the boot-media release; null uses the Bootstrap informational version, because
/// Foundry OSD provisions Bootstrap from the same release as the other runtimes. Boot-owned runtime roots (ISO media) never
/// keep a cached archive, and a repaired embedded runtime is never written back to the media.
/// </remarks>
internal sealed class RuntimeResolver(string winPeRoot, string runtimeRoot, string runtimeIdentifier, HttpClient httpClient, ILogger logger,
    Action<RuntimeDownloadProgress>? progress = null, Func<string, string?>? getEnvironmentVariable = null, Action<string>? warning = null,
    Action<string>? activity = null, string? releaseVersion = null) : IRuntimeResolver
{
    private const string ConnectApplication = "Foundry.Connect";
    private const string PostInstallApplication = "Foundry.PostInstall";
    private static readonly TimeSpan ConnectLookupDeadline = TimeSpan.FromSeconds(5);

    private readonly RuntimeTransfer transfer = new(httpClient, progress);
    private readonly Func<string, string?> environment = getEnvironmentVariable ?? Environment.GetEnvironmentVariable;
    private readonly string? mediaReleaseVersion = releaseVersion ??
        typeof(RuntimeResolver).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
    private readonly bool usePersistentCache = !IsBootOwnedRuntime(winPeRoot, runtimeRoot);
    private readonly ConcurrentDictionary<string, string> preparedWorkspaces = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns a complete authenticated payload in boot-owned storage, retained for the child's lifetime.</summary>
    public async Task<string> ResolveAsync(string applicationName, bool skipReleaseLookup, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var preparation = new RuntimePayloadPreparation(winPeRoot, transfer, logger, progress, activity,
            (executable, workspace) => preparedWorkspaces.TryAdd(executable, workspace));
        string prefix = applicationName switch
        {
            ConnectApplication => "FOUNDRY_CONNECT",
            "Foundry.Deploy" => "FOUNDRY_DEPLOY",
            PostInstallApplication => "FOUNDRY_POSTINSTALL",
            _ => throw new ArgumentOutOfRangeException(nameof(applicationName))
        };
        string archiveOverride = ReadEnvironment(prefix + "_ARCHIVE");
        if (archiveOverride.Length > 0)
        {
            // Overrides are explicit operator inputs, never an implicit escape from media authentication.
            string expected = RuntimePayloadPreparation.RequireHash(ReadEnvironment(prefix + "_ARCHIVE_SHA256"));
            logger.Information("Preparing authenticated archive override for {PayloadApplication}", applicationName);
            return await preparation.PrepareAsync(archiveOverride, expected, applicationName, cancellationToken).ConfigureAwait(false);
        }

        string? trustedHash = RuntimePayloadTrust.ReadArchiveHash(winPeRoot, applicationName, runtimeIdentifier);
        if (trustedHash is null)
        {
            if (skipReleaseLookup) throw Unavailable(applicationName, null);
            try
            {
                return await DownloadAsync(preparation, applicationName, prefix, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (IsResolutionFailure(exception, cancellationToken))
            {
                throw Unavailable(applicationName, exception);
            }
        }

        string? originalArchive = FindOriginalArchive(applicationName);
        if (originalArchive is not null)
        {
            try
            {
                string executable = await preparation.PrepareAsync(originalArchive, trustedHash, applicationName, cancellationToken).ConfigureAwait(false);
                logger.Information("Selected authenticated original boot-media payload for {PayloadApplication} on {RuntimeIdentifier}",
                    applicationName, runtimeIdentifier);
                return executable;
            }
            // Debug applications exist only on the media, so their authentication failure is final.
            catch (Exception exception) when (!skipReleaseLookup && IsResolutionFailure(exception, cancellationToken))
            {
                logger.Warning(exception, "Original boot-media payload for {PayloadApplication} failed authentication", applicationName);
            }
        }
        else if (!skipReleaseLookup)
        {
            logger.Warning("Original boot-media payload for {PayloadApplication} on {RuntimeIdentifier} is missing",
                applicationName, runtimeIdentifier);
        }

        if (skipReleaseLookup) throw Unavailable(applicationName, null);
        try
        {
            return await RepairAsync(preparation, applicationName, prefix, trustedHash, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsResolutionFailure(exception, cancellationToken))
        {
            throw Unavailable(applicationName, exception);
        }
    }

    /// <inheritdoc />
    public (string ArchivePath, string Sha256)? TryGetDebugArchive(string applicationName)
    {
        try
        {
            string? hash = RuntimePayloadTrust.ReadArchiveHash(winPeRoot, applicationName, runtimeIdentifier);
            if (hash is null) return null;
            return FindOriginalArchive(applicationName) is { } archive ? (archive, hash) : null;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            // The archive is an optional debug input; an unreadable trust manifest must not fail the boot.
            logger.Warning(exception, "Debug archive for {PayloadApplication} could not be located", applicationName);
            return null;
        }
    }

    /// <summary>Best-effort deletion of this resolver's registered workspace, including bundle extraction, after the child exits.</summary>
    internal void Release(string executable)
    {
        if (string.IsNullOrEmpty(executable) || !preparedWorkspaces.TryRemove(executable, out string? workspace)) return;
        try { Directory.Delete(workspace, recursive: true); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.Warning(exception, "Could not remove exited runtime workspace {RuntimeWorkspace}", workspace);
        }
    }

    /// <summary>
    /// Prepares a runtime the media does not embed. A cached archive is reused only when it matches the release asset digest
    /// of the media release, so a copy from another release is replaced by a fresh download.
    /// </summary>
    private async Task<string> DownloadAsync(RuntimePayloadPreparation preparation, string applicationName, string prefix,
        CancellationToken cancellationToken)
    {
        (RuntimeReleaseAsset asset, string tag) = await ReadReleaseAssetAsync(applicationName, prefix, cancellationToken).ConfigureAwait(false);
        string currentArchive = Path.Combine(Path.GetDirectoryName(
            RuntimePayloadTrust.GetBaselineArchivePath(runtimeRoot, applicationName, runtimeIdentifier))!, "current.zip");
        if (usePersistentCache && File.Exists(currentArchive))
        {
            try
            {
                string executable = await preparation.PrepareAsync(currentArchive, asset.Sha256, applicationName, cancellationToken).ConfigureAwait(false);
                logger.Information("Selected authenticated cached {PayloadApplication} of release {ReleaseTag} on {RuntimeIdentifier}",
                    applicationName, tag, runtimeIdentifier);
                return executable;
            }
            catch (Exception exception) when (IsResolutionFailure(exception, cancellationToken))
            {
                logger.Warning(exception, "Cached {PayloadApplication} does not match release {ReleaseTag}; downloading a replacement", applicationName, tag);
            }
        }

        logger.Information("Downloading {PayloadApplication} from release {ReleaseTag}", applicationName, tag);
        return await preparation.PrepareAsync(asset.DownloadUrl, asset.Sha256, applicationName, cancellationToken,
            usePersistentCache ? archive => PersistAsync(archive, currentArchive, applicationName, cancellationToken) : null).ConfigureAwait(false);
    }

    /// <summary>
    /// Downloads an embedded runtime from the media release. The archive must match the trust manifest hash, so a repair can
    /// only restore the bytes the media was created with.
    /// </summary>
    private async Task<string> RepairAsync(RuntimePayloadPreparation preparation, string applicationName, string prefix,
        string trustedHash, CancellationToken cancellationToken)
    {
        (RuntimeReleaseAsset asset, string tag) = await ReadReleaseAssetAsync(applicationName, prefix, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(trustedHash, asset.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The release asset does not match the boot media.");
        string executable = await preparation.PrepareAsync(asset.DownloadUrl, trustedHash, applicationName, cancellationToken).ConfigureAwait(false);
        logger.Warning("Repaired {PayloadApplication} on {RuntimeIdentifier} from release {ReleaseTag}", applicationName, runtimeIdentifier, tag);
        warning?.Invoke($"{applicationName} was repaired from release {tag}.");
        return executable;
    }

    /// <summary>Reads the runtime asset of the media release, honouring the release tag overrides of the application.</summary>
    private async Task<(RuntimeReleaseAsset Asset, string Tag)> ReadReleaseAssetAsync(string applicationName, string prefix,
        CancellationToken cancellationToken)
    {
        // PostInstall follows Deploy's override order, so Deploy accepts the runtime Bootstrap prepared for it.
        string[] overrides = applicationName == PostInstallApplication
            ? [prefix + "_RELEASE_TAG", "FOUNDRY_DEPLOY_RELEASE_TAG", FoundryReleaseTag.ReleaseTagVariable]
            : [prefix + "_RELEASE_TAG", FoundryReleaseTag.ReleaseTagVariable];
        FoundryReleaseSelection release = FoundryReleaseTag.Resolve(mediaReleaseVersion, environment, overrides);
        if (release.IsLatest)
            logger.Warning("Unpublished Bootstrap build uses the latest release for {PayloadApplication}", applicationName);

        using var lookupDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        // Connect may be needed to establish networking, so its release lookup must give up promptly when offline.
        if (applicationName == ConnectApplication) lookupDeadline.CancelAfter(ConnectLookupDeadline);
        using JsonDocument metadata = await transfer.ReadReleaseAsync(release.ApiUrl, lookupDeadline.Token).ConfigureAwait(false);
        return (RuntimeReleaseAsset.Parse(metadata.RootElement, applicationName, runtimeIdentifier), release.Tag ?? "latest");
    }

    /// <summary>Returns the original archive, preferring the boot image copy over the runtime root, or null when neither exists.</summary>
    private string? FindOriginalArchive(string applicationName)
    {
        string imageArchive = RuntimePayloadTrust.GetBaselineArchivePath(Path.Combine(winPeRoot, "Runtime"), applicationName, runtimeIdentifier);
        if (File.Exists(imageArchive)) return imageArchive;
        string archive = RuntimePayloadTrust.GetBaselineArchivePath(runtimeRoot, applicationName, runtimeIdentifier);
        return File.Exists(archive) ? archive : null;
    }

    private async Task PersistAsync(string source, string destination, string applicationName, CancellationToken cancellationToken)
    {
        try
        {
            await RuntimeArchiveCache.StoreAsync(source, destination, cancellationToken).ConfigureAwait(false);
            logger.Information("Verified runtime cached for {PayloadApplication}; reuse requires the release asset digest", applicationName);
        }
        catch (Exception exception) when (IsResolutionFailure(exception, cancellationToken))
        {
            logger.Warning(exception, "Verified runtime could not be cached for {PayloadApplication}; continuing from boot-owned storage", applicationName);
            warning?.Invoke("The verified runtime could not be saved to the cache. This boot can continue.");
        }
    }

    private string ReadEnvironment(string name) => environment(name)?.Trim() ?? "";

    private static bool IsBootOwnedRuntime(string winPeRoot, string runtimeRoot)
    {
        string bootRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(winPeRoot));
        string cacheRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(runtimeRoot));
        string bootDirectory = Path.EndsInDirectorySeparator(bootRoot) ? bootRoot : bootRoot + Path.DirectorySeparatorChar;
        return string.Equals(cacheRoot, bootRoot, StringComparison.OrdinalIgnoreCase) ||
            cacheRoot.StartsWith(bootDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private static InvalidDataException Unavailable(string applicationName, Exception? innerException) =>
        new($"No authenticated original {applicationName} payload is available. Recreate the boot media or connect to the network to obtain a verified runtime.",
            innerException);

    /// <summary>Identifies failures that make a payload source unusable; cancellation of the boot itself is never one.</summary>
    private static bool IsResolutionFailure(Exception exception, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested && exception is IOException or InvalidDataException or UnauthorizedAccessException or
            HttpRequestException or OperationCanceledException or JsonException or InvalidOperationException or KeyNotFoundException;
}
