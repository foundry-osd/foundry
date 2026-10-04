// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Net.Http;
using System.Text.Json;
using Foundry.Core.Services.Runtime;
using Foundry.Deploy.Services.Download;
using Foundry.Deploy.Services.Http;
using Foundry.Deploy.Services.Images;
using Foundry.Utilities.IO;
using Microsoft.Extensions.Logging;
using Serilog.Extensions.Logging;

namespace Foundry.Deploy.Services.Deployment.PreOobe;

/// <summary>Recovers a missing Bootstrap payload into WinPE storage, with optional verified USB archive reuse.</summary>
internal sealed class PostInstallRuntimeRecovery(string winPeRoot, string deployVersion, HttpClient client,
    IArtifactDownloadService downloadService, Func<string, string?> environment)
{
    private const string ApplicationName = "Foundry.PostInstall";
    private static readonly ILoggerFactory LoggerFactory = new SerilogLoggerFactory();
    private static readonly ILogger Logger = LoggerFactory.CreateLogger<PostInstallRuntimeRecovery>();
    private static readonly HttpClient ReleaseClient = CreateReleaseClient();

    internal PostInstallRuntimeRecovery() : this(@"X:\Foundry", FoundryDeployApplicationInfo.Version, ReleaseClient,
        new ArtifactDownloadService(LoggerFactory.CreateLogger<ArtifactDownloadService>()), Environment.GetEnvironmentVariable)
    { }

    /// <summary>Returns locked, compatible runtime files; publishing the USB cache is best effort and never overwrites original.zip.</summary>
    internal async Task<PreOobePreparedContent> AcquireAsync(string runtimeIdentifier, string? cacheRoot,
        CancellationToken cancellationToken, IProgress<DownloadProgress>? progress = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string workspace = Path.Combine(winPeRoot, "Execution", Guid.NewGuid().ToString("N"));
        string archive = Path.Combine(workspace, "archive.zip");
        string payload = Path.Combine(workspace, "Payload");
        PreOobePreparedContent? prepared = null;
        bool ready = false;
        try
        {
            _ = RuntimePayloadTrust.GetBaselineArchivePath(winPeRoot, ApplicationName, runtimeIdentifier);
            Directory.CreateDirectory(workspace);
            string archiveOverride = ReadEnvironment("FOUNDRY_POSTINSTALL_ARCHIVE");
            string? cacheArchive = string.IsNullOrWhiteSpace(cacheRoot) ? null :
                Path.Combine(cacheRoot, ApplicationName, runtimeIdentifier, "current.zip");
            if (archiveOverride.Length > 0)
            {
                await PrepareOverrideAsync(archiveOverride, archive, cancellationToken, progress).ConfigureAwait(false);
                cacheArchive = null;
            }
            else
            {
                string provisioning = Path.Combine(winPeRoot, "Config", "foundry.deploy.provisioning-source.txt");
                if (File.Exists(provisioning) && File.ReadAllText(provisioning).Trim().Equals("debug", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Debug media must provide PostInstall through Bootstrap or an authenticated archive override.");

                string tag = ResolveReleaseTag();
                Logger.LogInformation("Recovering missing PostInstall runtime. ReleaseTag={ReleaseTag}; RuntimeIdentifier={RuntimeIdentifier}", tag, runtimeIdentifier);
                RuntimeReleaseAsset asset = await ReadReleaseAssetAsync(tag, runtimeIdentifier, cancellationToken).ConfigureAwait(false);
                if (cacheArchive is not null)
                    await TryCopyCacheAsync(cacheArchive, archive, cancellationToken).ConfigureAwait(false);
                await downloadService.DownloadAsync(asset.DownloadUrl, archive, asset.Sha256,
                    artifactKind: "PostInstall", cancellationToken: cancellationToken, progress: progress).ConfigureAwait(false);
            }

            await RuntimeArchive.ExtractAsync(archive, payload, cancellationToken).ConfigureAwait(false);
            prepared = await PostInstallRuntimeSource.AcquireAsync(Path.Combine(payload, ApplicationName + ".exe"),
                runtimeIdentifier, cancellationToken).ConfigureAwait(false);
            if (cacheArchive is not null)
            {
                try { await RuntimeArchiveCache.StoreAsync(archive, cacheArchive, cancellationToken).ConfigureAwait(false); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    Logger.LogWarning(exception, "PostInstall runtime is verified but could not be saved to the USB cache; continuing from WinPE storage");
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Delete(archive);
            prepared.OwnedRuntimeWorkspace = workspace;
            ready = true;
            Logger.LogInformation("PostInstall runtime recovery completed. RuntimeIdentifier={RuntimeIdentifier}", runtimeIdentifier);
            return prepared;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested && exception is
            IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or HttpRequestException or JsonException or OperationCanceledException or TimeoutException)
        {
            throw new PostInstallRuntimeUnavailableException(exception);
        }
        finally
        {
            if (!ready)
            {
                prepared?.Dispose();
                DeleteWorkspace(workspace);
            }
        }
    }

    private async Task PrepareOverrideAsync(string source, string archive, CancellationToken cancellationToken, IProgress<DownloadProgress>? progress)
    {
        string hash = ReadEnvironment("FOUNDRY_POSTINSTALL_ARCHIVE_SHA256");
        if (hash.Length != 64 || hash.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException("A trusted SHA256 digest is required for a PostInstall archive override.");
        if (Uri.TryCreate(source, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps)
        {
            await downloadService.DownloadAsync(source, archive, hash, artifactKind: "PostInstall",
                cancellationToken: cancellationToken, progress: progress).ConfigureAwait(false);
            return;
        }
        if (!Path.IsPathFullyQualified(source)) throw new InvalidDataException("PostInstall archive overrides require a local absolute path or HTTPS URL.");
        CustomImageSourceLease.EnsureRegularPath(source);
        await RuntimeArchiveCache.StoreAsync(source, archive, cancellationToken).ConfigureAwait(false);
        if (!hash.Equals(await FileHash.ComputeSha256Async(archive, cancellationToken).ConfigureAwait(false), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("PostInstall archive override does not match the trusted SHA256 digest.");
    }

    private string ResolveReleaseTag()
    {
        foreach (string name in new[] { "FOUNDRY_POSTINSTALL_RELEASE_TAG", "FOUNDRY_DEPLOY_RELEASE_TAG", "FOUNDRY_RELEASE_TAG" })
        {
            string tag = ReadEnvironment(name);
            if (tag.Length > 0) return tag;
        }
        string version = deployVersion.Split('+')[0];
        if (!Version.TryParse(version, out Version? parsed) || parsed.Revision < 0)
            throw new InvalidDataException("The running Deploy release cannot be identified for PostInstall recovery.");
        return "v" + version;
    }

    private Task<RuntimeReleaseAsset> ReadReleaseAssetAsync(string tag, string runtimeIdentifier, CancellationToken cancellationToken) =>
        HttpRetryPolicy.ExecuteAsync(async token =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                "https://api.github.com/repos/foundry-osd/foundry/releases/tags/" + Uri.EscapeDataString(tag));
            request.Headers.UserAgent.ParseAdd("FoundryDeploy/1.0");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using HttpResponseMessage response = await client.SendAsync(request, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            using JsonDocument release = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false));
            return RuntimeReleaseAsset.Parse(release.RootElement, ApplicationName, runtimeIdentifier);
        }, Logger, "PostInstall release lookup", cancellationToken, retryCount: 2, retryDelay: TimeSpan.FromSeconds(2));

    private static async Task TryCopyCacheAsync(string source, string destination, CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(source)) return;
            CustomImageSourceLease.EnsureRegularPath(source);
            await RuntimeArchiveCache.StoreAsync(source, destination, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Logger.LogWarning(exception, "PostInstall USB cache could not be read; downloading a replacement");
        }
    }

    private string ReadEnvironment(string name) => environment(name)?.Trim() ?? "";

    private static HttpClient CreateReleaseClient()
    {
        HttpClient httpClient = DeploymentHttpClientFactory.Create(TimeSpan.FromSeconds(45));
        httpClient.MaxResponseContentBufferSize = 2 * 1024 * 1024;
        return httpClient;
    }

    /// <summary>Removes only a recovery-owned workspace after file leases have been released.</summary>
    internal static void DeleteWorkspace(string workspace)
    {
        try { if (Directory.Exists(workspace)) Directory.Delete(workspace, recursive: true); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Logger.LogWarning(exception, "Could not remove the PostInstall recovery workspace");
        }
    }
}

/// <summary>Distinguishes missing-runtime recovery failures from invalid user post-installation content.</summary>
internal sealed class PostInstallRuntimeUnavailableException(Exception innerException)
    : IOException("The required PostInstall runtime could not be recovered.", innerException);
