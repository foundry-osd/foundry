// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Foundry.Core.Services.Runtime;

/// <summary>Reads the version of the latest published Foundry release, or <see langword="null"/> when it is unknown.</summary>
/// <param name="cancellationToken">Cancels the read; the only failure a reader lets escape.</param>
public delegate Task<string?> LatestReleaseVersionReader(CancellationToken cancellationToken);

/// <summary>
/// Reads the latest published Foundry release from the GitHub API so a runtime can tell that newer boot media exist.
/// </summary>
/// <remarks>
/// Only the release metadata is read, never an asset: boot media keep running the release that created them. The
/// lookup is best effort and short, because a runtime often starts without network access.
/// </remarks>
public static class LatestFoundryRelease
{
    /// <summary>The longest time one lookup may take before it yields no version.</summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Reads the latest release tag and returns its <c>YY.M.D.build</c> version without the leading <c>v</c>.
    /// </summary>
    /// <param name="client">The client that sends the request.</param>
    /// <param name="logger">Receives a Debug entry when no version can be read.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>
    /// The published release version, or <see langword="null"/> after an HTTP, JSON or timeout failure or for a tag that
    /// is not a published release version.
    /// </returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public static Task<string?> ReadVersionAsync(HttpClient client, ILogger? logger, CancellationToken cancellationToken) =>
        ReadVersionAsync(client, logger, RequestTimeout, cancellationToken);

    /// <summary>Reads the latest release version with an explicit timeout, for tests.</summary>
    internal static async Task<string?> ReadVersionAsync(
        HttpClient client,
        ILogger? logger,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new FoundryReleaseSelection(null).ApiUrl);
            request.Headers.UserAgent.ParseAdd("FoundryOSD/1.0");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using HttpResponseMessage response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                logger?.LogDebug("The latest Foundry release lookup returned HTTP {StatusCode}.", (int)response.StatusCode);
                return null;
            }

            await using Stream content = await response.Content.ReadAsStreamAsync(timeoutSource.Token).ConfigureAwait(false);
            using JsonDocument release = await JsonDocument
                .ParseAsync(content, cancellationToken: timeoutSource.Token)
                .ConfigureAwait(false);
            string? version = ReadVersion(release.RootElement);
            if (version is null)
            {
                logger?.LogDebug("The latest Foundry release tag is not a published release version.");
            }

            return version;
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Offline, rate-limited, timed out or malformed: the runtime simply shows no newer release notice.
            logger?.LogDebug(
                "The latest Foundry release could not be read. FailureType={FailureType}",
                exception.GetType().Name);
            return null;
        }
    }

    private static string? ReadVersion(JsonElement release)
    {
        if (release.ValueKind != JsonValueKind.Object ||
            !release.TryGetProperty("tag_name", out JsonElement tag) ||
            tag.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string value = tag.GetString()!.Trim();
        if (value.StartsWith('v'))
        {
            value = value[1..];
        }

        return BootMediaFreshnessPolicy.TryParseReleaseVersion(value, out _) ? value : null;
    }
}
