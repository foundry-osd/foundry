// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Net.Http;
using System.Text.Json;
using Foundry.Core.Models.Autopilot;
using Foundry.Core.Services.Http;
using Foundry.Core.Services.Runtime;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.Download;
using Foundry.Deploy.Services.Http;
using Foundry.Deploy.Services.Images;
using Foundry.Utilities.IO;
using Microsoft.Extensions.Logging;
using Serilog.Extensions.Logging;

namespace Foundry.Deploy.Services.Autopilot;

/// <summary>
/// Locates and installs the <c>Foundry.AutopilotInteractive</c> runtime directly into the target operating system.
/// </summary>
/// <remarks>
/// Production media download the archive of release <c>v&lt;Deploy version&gt;</c> (or the <c>FOUNDRY_DEPLOY_RELEASE_TAG</c>, then
/// <c>FOUNDRY_RELEASE_TAG</c> override), never <c>latest</c>. Debug media never download: Bootstrap forwards the media archive and
/// its SHA256 through <see cref="AutopilotInteractiveLayout.ArchiveEnvironmentVariable"/> and
/// <see cref="AutopilotInteractiveLayout.ArchiveSha256EnvironmentVariable"/>, and this class verifies the hash while copying.
/// The archive only ever lands in the target runtime folder: never in WinPE storage, the USB cache or the network cache.
/// </remarks>
internal sealed class AutopilotInteractiveRuntimeSource(string winPeRoot, string deployVersion, HttpClient client,
    IArtifactDownloadService download, Func<string, string?> environment)
{
    private const string ArtifactKind = "AutopilotInteractive";
    private static readonly TimeSpan[] DownloadRetryDelays = [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)];
    private static readonly ILoggerFactory LoggerFactory = new SerilogLoggerFactory();
    private static readonly ILogger Logger = LoggerFactory.CreateLogger<AutopilotInteractiveRuntimeSource>();
    private static readonly HttpClient ReleaseClient = CreateReleaseClient();

    /// <summary>Creates the production source. Its downloader has no network cache, so the archive never reaches a shared cache.</summary>
    internal AutopilotInteractiveRuntimeSource() : this(@"X:\Foundry", FoundryDeployApplicationInfo.Version, ReleaseClient,
        new ArtifactDownloadService(LoggerFactory.CreateLogger<ArtifactDownloadService>()), Environment.GetEnvironmentVariable)
    { }

    /// <summary>Waits between download attempts; replaced by tests.</summary>
    internal Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = Task.Delay;

    /// <summary>
    /// Finds the archive to install without touching any disk. A forwarded media archive wins; debug media without one fail,
    /// because they must never fetch a published build; otherwise the release must publish exactly one archive for
    /// <paramref name="runtimeIdentifier"/> with an HTTPS address and a <c>sha256:</c> digest.
    /// </summary>
    /// <param name="runtimeIdentifier">Windows runtime identifier, <c>win-x64</c> or <c>win-arm64</c>.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The archive origin to hand to <see cref="InstallAsync"/>.</returns>
    /// <exception cref="InvalidDataException">The forwarded archive is incomplete, debug media have none, or the release asset is missing or untrusted.</exception>
    /// <exception cref="HttpRequestException">The release metadata could not be read.</exception>
    internal async Task<AutopilotInteractiveRuntimeOrigin> ResolveAsync(string runtimeIdentifier, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string archive = ReadEnvironment(AutopilotInteractiveLayout.ArchiveEnvironmentVariable);
        string sha256 = ReadEnvironment(AutopilotInteractiveLayout.ArchiveSha256EnvironmentVariable);
        if (archive.Length > 0 || sha256.Length > 0) return ResolveForwardedArchive(archive, sha256);

        string provisioning = Path.Combine(winPeRoot, "Config", "foundry.deploy.provisioning-source.txt");
        if (File.Exists(provisioning) && File.ReadAllText(provisioning).Trim().Equals("debug", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Debug media must provide Foundry.AutopilotInteractive through Bootstrap.");

        string tag = DeployReleaseTag.Resolve(deployVersion, environment, "FOUNDRY_DEPLOY_RELEASE_TAG", "FOUNDRY_RELEASE_TAG");
        Logger.LogInformation("Resolving the Foundry.AutopilotInteractive release asset. ReleaseTag={ReleaseTag}; RuntimeIdentifier={RuntimeIdentifier}",
            tag, runtimeIdentifier);
        RuntimeReleaseAsset asset = await ReadReleaseAssetAsync(tag, runtimeIdentifier, cancellationToken).ConfigureAwait(false);
        Logger.LogInformation("Foundry.AutopilotInteractive release asset resolved. ReleaseTag={ReleaseTag}; RuntimeIdentifier={RuntimeIdentifier}",
            tag, runtimeIdentifier);
        return new AutopilotInteractiveRuntimeOrigin(asset.DownloadUrl, asset.Sha256, IsLocalArchive: false);
    }

    /// <summary>
    /// Obtains the SHA256-verified archive as <c>download-&lt;guid&gt;.zip</c> inside <paramref name="targetRuntimeDirectory"/>,
    /// extracts it there and requires <see cref="AutopilotInteractiveLayout.ExecutableFileName"/>. A release download is tried up to
    /// three times, 5 then 15 seconds apart; a media archive is copied once. The archive is always deleted, and on failure or
    /// cancellation the runtime directory, which this application owns exclusively, is emptied so no partial runtime remains.
    /// </summary>
    /// <param name="origin">Archive found by <see cref="ResolveAsync"/>.</param>
    /// <param name="targetRuntimeDirectory">Dedicated runtime directory in the target operating system.</param>
    /// <param name="progress">Receives download progress, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the installation.</param>
    internal async Task InstallAsync(AutopilotInteractiveRuntimeOrigin origin, string targetRuntimeDirectory,
        IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetRuntimeDirectory);
        Directory.CreateDirectory(targetRuntimeDirectory);
        string archive = Path.Combine(targetRuntimeDirectory, $"download-{Guid.NewGuid():N}.zip");
        bool installed = false;
        Logger.LogInformation("Installing Foundry.AutopilotInteractive into the target operating system. LocalArchive={LocalArchive}",
            origin.IsLocalArchive);
        try
        {
            if (origin.IsLocalArchive)
                await CopyForwardedArchiveAsync(origin, archive, cancellationToken).ConfigureAwait(false);
            else
                await DownloadAsync(origin, archive, progress, cancellationToken).ConfigureAwait(false);

            await RuntimeArchive.ExtractAsync(archive, targetRuntimeDirectory, cancellationToken).ConfigureAwait(false);
            if (!File.Exists(Path.Combine(targetRuntimeDirectory, AutopilotInteractiveLayout.ExecutableFileName)))
                throw new InvalidDataException($"The Foundry.AutopilotInteractive archive does not contain {AutopilotInteractiveLayout.ExecutableFileName}.");
            installed = true;
            Logger.LogInformation("Foundry.AutopilotInteractive installed. RuntimeDirectory={RuntimeDirectory}", targetRuntimeDirectory);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Logger.LogInformation("Foundry.AutopilotInteractive installation cancelled.");
            throw;
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Foundry.AutopilotInteractive installation failed. LocalArchive={LocalArchive}", origin.IsLocalArchive);
            throw;
        }
        finally
        {
            TryDelete(archive);
            if (!installed) ClearRuntimeDirectory(targetRuntimeDirectory);
        }
    }

    private static AutopilotInteractiveRuntimeOrigin ResolveForwardedArchive(string archive, string sha256)
    {
        if (sha256.Length != 64 || sha256.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException("A trusted SHA256 digest is required for the forwarded Foundry.AutopilotInteractive archive.");
        if (!Path.IsPathFullyQualified(archive))
            throw new InvalidDataException("The forwarded Foundry.AutopilotInteractive archive requires a local absolute path.");
        CustomImageSourceLease.EnsureRegularPath(archive);
        if (!File.Exists(archive))
            throw new FileNotFoundException("The forwarded Foundry.AutopilotInteractive archive was not found.");
        Logger.LogInformation("Using the Foundry.AutopilotInteractive archive forwarded by Bootstrap.");
        return new AutopilotInteractiveRuntimeOrigin(archive, sha256, IsLocalArchive: true);
    }

    private static async Task CopyForwardedArchiveAsync(AutopilotInteractiveRuntimeOrigin origin, string archive, CancellationToken cancellationToken)
    {
        CustomImageSourceLease.EnsureRegularPath(origin.Source);
        await RuntimeArchiveCache.StoreAsync(origin.Source, archive, cancellationToken).ConfigureAwait(false);
        string actual = await FileHash.ComputeSha256Async(archive, cancellationToken).ConfigureAwait(false);
        if (!actual.Equals(origin.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The forwarded Foundry.AutopilotInteractive archive does not match its trusted SHA256 digest.");
    }

    private async Task DownloadAsync(AutopilotInteractiveRuntimeOrigin origin, string archive, IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                await download.DownloadAsync(origin.Source, archive, origin.Sha256, artifactKind: ArtifactKind,
                    cancellationToken: cancellationToken, progress: progress).ConfigureAwait(false);
                return;
            }
            catch (Exception exception) when (attempt <= DownloadRetryDelays.Length && !cancellationToken.IsCancellationRequested)
            {
                TimeSpan delay = DownloadRetryDelays[attempt - 1];
                Logger.LogWarning(exception,
                    "Foundry.AutopilotInteractive download attempt {Attempt}/{TotalAttempts} failed. Retrying in {DelaySeconds} seconds.",
                    attempt, DownloadRetryDelays.Length + 1, delay.TotalSeconds);
                TryDelete(archive);
                await Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
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
            return RuntimeReleaseAsset.Parse(release.RootElement, AutopilotInteractiveLayout.ApplicationName, runtimeIdentifier);
        }, Logger, "Foundry.AutopilotInteractive release lookup", cancellationToken, retryCount: 2, retryDelay: TimeSpan.FromSeconds(2));

    private string ReadEnvironment(string name) => environment(name)?.Trim() ?? string.Empty;

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Logger.LogWarning(exception, "Could not remove the Foundry.AutopilotInteractive download");
        }
    }

    private static void ClearRuntimeDirectory(string directory)
    {
        try
        {
            if (!Directory.Exists(directory)) return;
            foreach (string file in Directory.EnumerateFiles(directory)) File.Delete(file);
            foreach (string child in Directory.EnumerateDirectories(directory)) Directory.Delete(child, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Logger.LogWarning(exception, "Could not remove the partial Foundry.AutopilotInteractive runtime");
        }
    }

    private static HttpClient CreateReleaseClient()
    {
        HttpClient httpClient = DeploymentHttpClientFactory.Create(TimeSpan.FromSeconds(45));
        httpClient.MaxResponseContentBufferSize = 2 * 1024 * 1024;
        return httpClient;
    }
}
