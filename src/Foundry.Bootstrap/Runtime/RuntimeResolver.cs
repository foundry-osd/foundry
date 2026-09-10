// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Text.Json;
using Foundry.Utilities.IO;
using Serilog;

namespace Foundry.Bootstrap.Runtime;

/// <summary>Resolves the existing WinPE payload layout and release policy.</summary>
internal sealed class RuntimeResolver(string winPeRoot, string runtimeRoot, string runtimeIdentifier, HttpClient httpClient, ILogger logger,
    Action<RuntimeDownloadProgress>? progress = null, Func<string, string?>? getEnvironmentVariable = null, Action<string>? warning = null) : IRuntimeResolver
{
    private readonly RuntimeTransfer transfer = new(httpClient, progress);
    private readonly Func<string, string?> environment = getEnvironmentVariable ?? Environment.GetEnvironmentVariable;

    /// <inheritdoc />
    public async Task<string> ResolveAsync(string applicationName, bool skipReleaseLookup, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (applicationName is not ("Foundry.Connect" or "Foundry.Deploy")) throw new ArgumentOutOfRangeException(nameof(applicationName));
        if (runtimeIdentifier is not ("win-x64" or "win-arm64")) throw new PlatformNotSupportedException($"Unsupported runtime '{runtimeIdentifier}'.");
        string applicationRoot = Path.Combine(runtimeRoot, applicationName);
        Directory.CreateDirectory(applicationRoot);
        string cacheRoot = Path.Combine(applicationRoot, runtimeIdentifier);
        string assetName = $"{applicationName}-{runtimeIdentifier}.zip";
        string downloadPath = Path.Combine(applicationRoot, assetName + ".download");
        string prefix = applicationName == "Foundry.Connect" ? "FOUNDRY_CONNECT" : "FOUNDRY_DEPLOY";
        string archiveOverride = ReadEnvironment(prefix + "_ARCHIVE");
        string tagOverride = ReadEnvironment(prefix + "_RELEASE_TAG");
        if (tagOverride.Length == 0) tagOverride = ReadEnvironment("FOUNDRY_RELEASE_TAG");
        string? embeddedArchive = applicationName == "Foundry.Deploy" ? Path.Combine(winPeRoot, "Seed", "Foundry.Deploy.zip") : null;
        if (skipReleaseLookup && archiveOverride.Length == 0 && File.Exists(embeddedArchive)) archiveOverride = embeddedArchive;

        try
        {
            if (archiveOverride.Length > 0)
            {
                logger.Information("Resolving {PayloadApplication} from an archive override", applicationName);
                await transfer.SaveAsync(archiveOverride, downloadPath, applicationName, cancellationToken).ConfigureAwait(false);
                string hash = await VerifyHashAsync(downloadPath, ReadEnvironment(prefix + "_ARCHIVE_SHA256"), cancellationToken).ConfigureAwait(false);
                return await UpdateCacheAsync(downloadPath, cacheRoot, applicationName, assetName, "", "", hash, cancellationToken).ConfigureAwait(false);
            }
            if (skipReleaseLookup) return ResolveCached(cacheRoot, applicationName);

            JsonDocument release;
            string releaseUrl = "https://api.github.com/repos/foundry-osd/foundry/releases/" +
                (tagOverride.Length == 0 ? "latest" : "tags/" + Uri.EscapeDataString(tagOverride));
            try
            {
                release = await transfer.ReadReleaseAsync(releaseUrl, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                logger.Warning(exception, "Release lookup failed for {PayloadApplication}; using available fallback", applicationName);
                return await FallbackAsync("Release lookup failed.").ConfigureAwait(false);
            }

            using (release)
            {
                string tag = release.RootElement.GetProperty("tag_name").GetString() ?? "";
                string version = tag.Trim();
                if (version.StartsWith('v') || version.StartsWith('V')) version = version[1..];
                JsonElement asset = release.RootElement.GetProperty("assets").EnumerateArray()
                    .FirstOrDefault(item => string.Equals(item.GetProperty("name").GetString(), assetName, StringComparison.OrdinalIgnoreCase));
                if (asset.ValueKind == JsonValueKind.Undefined) throw new InvalidDataException($"Release does not contain '{assetName}'.");
                string? digest = asset.TryGetProperty("digest", out JsonElement digestElement) ? digestElement.GetString() : null;
                string? expectedHash = digest?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true ? digest[7..].Trim() : null;
                if (IsCacheCurrent(cacheRoot, applicationName, assetName, tag, version, expectedHash)) return ResolveCached(cacheRoot, applicationName);
                try
                {
                    if (string.IsNullOrWhiteSpace(expectedHash)) logger.Warning("No supported SHA256 digest supplied for {PayloadApplication}; continuing without digest validation", applicationName);
                    string url = asset.GetProperty("browser_download_url").GetString() ?? throw new InvalidDataException("Release asset has no download URL.");
                    await transfer.SaveAsync(url, downloadPath, applicationName, cancellationToken).ConfigureAwait(false);
                    string hash = await VerifyHashAsync(downloadPath, expectedHash, cancellationToken).ConfigureAwait(false);
                    return await UpdateCacheAsync(downloadPath, cacheRoot, applicationName, assetName, tag, version, hash, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                {
                    logger.Warning(exception, "Cache refresh failed for {PayloadApplication}; using available fallback", applicationName);
                    return await FallbackAsync("Application update failed.").ConfigureAwait(false);
                }
            }
        }
        finally
        {
            try { File.Delete(downloadPath); }
            catch (Exception exception) { logger.Warning(exception, "Could not remove temporary payload download for {PayloadApplication}", applicationName); }
        }

        async Task<string> FallbackAsync(string reason)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(Path.Combine(cacheRoot, applicationName + ".exe")))
            {
                string executable = ResolveCached(cacheRoot, applicationName);
                warning?.Invoke(reason + " Continuing with the cached application.");
                return executable;
            }
            if (!File.Exists(embeddedArchive)) return ResolveCached(cacheRoot, applicationName);
            logger.Warning("Using embedded archive fallback for {PayloadApplication}", applicationName);
            await transfer.SaveAsync(embeddedArchive!, downloadPath, applicationName, cancellationToken).ConfigureAwait(false);
            string hash = await VerifyHashAsync(downloadPath, null, cancellationToken).ConfigureAwait(false);
            string embeddedExecutable = await UpdateCacheAsync(downloadPath, cacheRoot, applicationName, assetName, "", "", hash, cancellationToken).ConfigureAwait(false);
            warning?.Invoke(reason + " Continuing with the application included on the boot media.");
            return embeddedExecutable;
        }
    }

    private string ReadEnvironment(string name) => environment(name)?.Trim() ?? "";

    private static string ResolveCached(string cacheRoot, string applicationName)
    {
        string executable = Path.Combine(cacheRoot, applicationName + ".exe");
        if (!File.Exists(executable)) throw new FileNotFoundException($"No cached {applicationName} executable is available.", executable);
        return executable;
    }

    private static bool IsCacheCurrent(string cacheRoot, string applicationName, string assetName, string tag, string version, string? expectedHash)
    {
        string executable = Path.Combine(cacheRoot, applicationName + ".exe");
        if (!File.Exists(executable)) return false;
        string manifestPath = Path.Combine(cacheRoot, "manifest");
        var manifest = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(manifestPath))
        {
            foreach (string line in File.ReadLines(manifestPath))
            {
                int separator = line.IndexOf('=');
                if (separator > 0) manifest[line[..separator].Trim()] = line[(separator + 1)..].Trim();
            }
        }
        if (manifest.Count > 0)
        {
            if (!Equal(manifest.GetValueOrDefault("Asset"), assetName)) return false;
            if (!string.IsNullOrWhiteSpace(expectedHash)) return Equal(manifest.GetValueOrDefault("ArchiveSha256"), expectedHash);
            if (!string.IsNullOrWhiteSpace(manifest.GetValueOrDefault("Tag"))) return Equal(manifest["Tag"], tag);
            if (!string.IsNullOrWhiteSpace(manifest.GetValueOrDefault("Version"))) return Equal(manifest["Version"], version);
        }
        string? cachedVersion = FileVersionInfo.GetVersionInfo(executable).FileVersion?.Trim();
        return !string.IsNullOrWhiteSpace(cachedVersion) && Equal(cachedVersion, version);
    }

    private static bool Equal(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static async Task<string> VerifyHashAsync(string archive, string? expected, CancellationToken cancellationToken)
    {
        string actual = await FileHash.ComputeSha256Async(archive, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(expected))
        {
            string normalized = expected.Trim();
            if (normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character))) throw new InvalidDataException("Invalid archive SHA256 value.");
            if (!Equal(actual, normalized)) throw new InvalidDataException("Archive SHA256 mismatch.");
        }
        return actual;
    }

    private async Task<string> UpdateCacheAsync(string archive, string cacheRoot, string applicationName, string assetName,
        string tag, string version, string hash, CancellationToken cancellationToken)
    {
        string staging = cacheRoot + ".staging";
        RuntimeCache.DeleteDirectory(staging);
        try
        {
            Directory.CreateDirectory(staging);
            string tool = Path.Combine(winPeRoot, "Tools", "7zip", runtimeIdentifier == "win-x64" ? "x64" : "arm64", "7za.exe");
            await RuntimeArchive.ExtractAsync(tool, archive, staging, cancellationToken).ConfigureAwait(false);
            string executable = ResolveCached(staging, applicationName);
            if (string.IsNullOrWhiteSpace(version)) version = FileVersionInfo.GetVersionInfo(executable).FileVersion?.Trim() ?? "";
            await File.WriteAllLinesAsync(Path.Combine(staging, "manifest"),
                [$"Tag={tag}", $"Version={version}", $"Asset={assetName}", $"ArchiveSha256={hash}", $"UpdatedUtc={DateTime.UtcNow:O}"], cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            new RuntimeCache().Promote(staging, cacheRoot);
            logger.Information("Updated runtime cache for {PayloadApplication}", applicationName);
            return ResolveCached(cacheRoot, applicationName);
        }
        finally
        {
            try { RuntimeCache.DeleteDirectory(staging); }
            catch (Exception exception) { logger.Warning(exception, "Could not remove staging directory for {PayloadApplication}", applicationName); }
        }
    }
}
