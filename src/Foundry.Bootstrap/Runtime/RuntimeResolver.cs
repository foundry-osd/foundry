// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Foundry.Core.Services.Runtime;
using Serilog;

namespace Foundry.Bootstrap.Runtime;

/// <summary>Authenticates original media archives offline and updated archives against fresh release metadata.</summary>
internal sealed class RuntimeResolver(string winPeRoot, string runtimeRoot, string runtimeIdentifier, HttpClient httpClient, ILogger logger,
    Action<RuntimeDownloadProgress>? progress = null, Func<string, string?>? getEnvironmentVariable = null, Action<string>? warning = null,
    Action<string>? activity = null) : IRuntimeResolver
{
    private readonly RuntimeTransfer transfer = new(httpClient, progress);
    private readonly Func<string, string?> environment = getEnvironmentVariable ?? Environment.GetEnvironmentVariable;

    /// <summary>Returns a complete authenticated payload in boot-owned storage, retained for the child's lifetime.</summary>
    public Task<string> ResolveAsync(string applicationName, bool skipReleaseLookup, CancellationToken cancellationToken) =>
        ResolveCoreAsync(applicationName, skipReleaseLookup, retainPayload: true, cancellationToken);

    /// <inheritdoc />
    public async Task RefreshAsync(string applicationName, CancellationToken cancellationToken) =>
        await ResolveCoreAsync(applicationName, skipReleaseLookup: false, retainPayload: false, cancellationToken).ConfigureAwait(false);

    private async Task<string> ResolveCoreAsync(string applicationName, bool skipReleaseLookup, bool retainPayload, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string originalArchive = RuntimePayloadTrust.GetBaselineArchivePath(runtimeRoot, applicationName, runtimeIdentifier);
        string currentArchive = Path.Combine(Path.GetDirectoryName(originalArchive)!, "current.zip");
        var preparation = new RuntimePayloadPreparation(winPeRoot, transfer, logger, progress, activity, retainPayload);
        string prefix = applicationName == "Foundry.Connect" ? "FOUNDRY_CONNECT" : "FOUNDRY_DEPLOY";
        string archiveOverride = ReadEnvironment(prefix + "_ARCHIVE");
        if (archiveOverride.Length > 0)
        {
            // Overrides are explicit operator inputs, never an implicit escape from media authentication.
            string expected = RuntimePayloadPreparation.RequireHash(ReadEnvironment(prefix + "_ARCHIVE_SHA256"));
            logger.Information("Preparing authenticated archive override for {PayloadApplication}", applicationName);
            return await preparation.PrepareAsync(archiveOverride, expected, applicationName, cancellationToken).ConfigureAwait(false);
        }
        if (skipReleaseLookup) return await OriginalAsync().ConfigureAwait(false);

        string tag = ReadEnvironment(prefix + "_RELEASE_TAG");
        if (tag.Length == 0) tag = ReadEnvironment("FOUNDRY_RELEASE_TAG");
        string releaseUrl = "https://api.github.com/repos/foundry-osd/foundry/releases/" +
            (tag.Length == 0 ? "latest" : "tags/" + Uri.EscapeDataString(tag));
        try
        {
            // Connect may be needed to establish networking, so its online lookup must yield promptly to the original payload.
            using var lookupDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (applicationName == "Foundry.Connect") lookupDeadline.CancelAfter(TimeSpan.FromSeconds(5));
            using JsonDocument release = await transfer.ReadReleaseAsync(releaseUrl, lookupDeadline.Token).ConfigureAwait(false);
            string assetName = $"{applicationName}-{runtimeIdentifier}.zip";
            JsonElement[] assets = release.RootElement.GetProperty("assets").EnumerateArray()
                .Where(item => string.Equals(item.GetProperty("name").GetString(), assetName, StringComparison.Ordinal)).ToArray();
            if (assets.Length != 1) throw new InvalidDataException($"Release must contain exactly one '{assetName}' asset.");
            JsonElement asset = assets[0];
            string? digest = asset.TryGetProperty("digest", out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() : null;
            string expectedHash = RuntimePayloadPreparation.RequireHash(
                digest?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true ? digest[7..] : null);

            if (File.Exists(currentArchive))
            {
                try
                {
                    string executable = await preparation.PrepareAsync(currentArchive, expectedHash, applicationName, cancellationToken).ConfigureAwait(false);
                    logger.Information("Selected authenticated cached update for {PayloadApplication} on {RuntimeIdentifier}", applicationName, runtimeIdentifier);
                    return executable;
                }
                catch (Exception exception) when (CanFallBack(exception, cancellationToken))
                {
                    logger.Warning(exception, "Cached runtime update could not be authenticated for {PayloadApplication}; downloading a replacement", applicationName);
                }
            }

            string? downloadUrl = asset.GetProperty("browser_download_url").GetString();
            if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
                throw new InvalidDataException("Runtime release asset must use HTTPS.");
            return await preparation.PrepareAsync(downloadUrl!, expectedHash, applicationName, cancellationToken,
                archive => PersistUpdateAsync(archive, currentArchive, applicationName, cancellationToken)).ConfigureAwait(false);
        }
        catch (Exception exception) when (CanFallBack(exception, cancellationToken))
        {
            logger.Warning(exception, "Online runtime verification failed for {PayloadApplication}; trying the original boot-media payload", applicationName);
            string executable = await OriginalAsync().ConfigureAwait(false);
            warning?.Invoke("Online runtime verification failed. Continuing with the original application provisioned on the boot media.");
            return executable;
        }

        async Task<string> OriginalAsync()
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? expected = RuntimePayloadTrust.ReadArchiveHash(winPeRoot, applicationName, runtimeIdentifier);
            string imageArchive = RuntimePayloadTrust.GetBaselineArchivePath(Path.Combine(winPeRoot, "Runtime"), applicationName, runtimeIdentifier);
            string source = File.Exists(imageArchive) ? imageArchive : originalArchive;
            if (expected is null || !File.Exists(source))
                throw new InvalidDataException($"No authenticated original {applicationName} payload is available. Recreate the boot media or connect to the network to obtain a verified runtime.");
            string executable = await preparation.PrepareAsync(source, expected, applicationName, cancellationToken).ConfigureAwait(false);
            logger.Information("Selected authenticated original boot-media payload for {PayloadApplication} on {RuntimeIdentifier}", applicationName, runtimeIdentifier);
            return executable;
        }
    }

    private string ReadEnvironment(string name) => environment(name)?.Trim() ?? "";

    private async Task PersistUpdateAsync(string source, string destination, string applicationName, CancellationToken cancellationToken)
    {
        try
        {
            await RuntimeArchiveCache.StoreAsync(source, destination, cancellationToken).ConfigureAwait(false);
            logger.Information("Verified runtime update cached for {PayloadApplication}; future reuse requires online verification", applicationName);
        }
        catch (Exception exception) when (CanFallBack(exception, cancellationToken))
        {
            logger.Warning(exception, "Verified runtime could not be cached for {PayloadApplication}; continuing from boot-owned storage", applicationName);
            warning?.Invoke("The verified runtime could not be saved to the cache. This boot can continue.");
        }
    }

    private static bool CanFallBack(Exception exception, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested && exception is IOException or InvalidDataException or UnauthorizedAccessException or
            HttpRequestException or OperationCanceledException or JsonException or InvalidOperationException or KeyNotFoundException;
}
