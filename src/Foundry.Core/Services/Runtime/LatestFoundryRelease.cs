// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net.Http;
using Microsoft.Extensions.Logging;

namespace Foundry.Core.Services.Runtime;

/// <summary>Reads the version of the latest published Foundry release, or <see langword="null"/> when it is unknown.</summary>
/// <param name="cancellationToken">Cancels the read; the only failure a reader lets escape.</param>
public delegate Task<string?> LatestReleaseVersionReader(CancellationToken cancellationToken);

/// <summary>
/// Reads the latest published Foundry release from the github.com release redirect so a runtime can tell that newer
/// boot media exist.
/// </summary>
/// <remarks>
/// The redirect of <see cref="LatestReleasePageUrl"/> names the latest release tag without calling the GitHub REST API,
/// whose unauthenticated limit is shared by every device behind the same public address. Only the redirect target is
/// read, never an asset: boot media keep running the release that created them. The lookup is best effort and short,
/// because a runtime often starts without network access.
/// </remarks>
public static class LatestFoundryRelease
{
    /// <summary>The longest time one lookup may take before it yields no version.</summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    /// <summary>The github.com page that redirects to the latest published release tag.</summary>
    public const string LatestReleasePageUrl = "https://github.com/foundry-osd/foundry/releases/latest";

    private const string ReleaseTagPathPrefix = "/foundry-osd/foundry/releases/tag/";

    /// <summary>
    /// Reads the latest release tag from the redirect target and returns its <c>YY.M.D.build</c> version without the
    /// leading <c>v</c>.
    /// </summary>
    /// <param name="client">
    /// The client that sends the request. It may follow redirects or not: the target is read from the final request
    /// address or from the <c>Location</c> header.
    /// </param>
    /// <param name="logger">Receives a Debug entry when no version can be read.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>
    /// The published release version, or <see langword="null"/> after an HTTP or timeout failure, a missing redirect, or a
    /// target that is not a published release tag of the Foundry repository.
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
            using var request = new HttpRequestMessage(HttpMethod.Head, LatestReleasePageUrl);
            request.Headers.UserAgent.ParseAdd("FoundryOSD/1.0");
            using HttpResponseMessage response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token)
                .ConfigureAwait(false);
            Uri? target = ReadTarget(response);
            if (target is null)
            {
                logger?.LogDebug("The latest Foundry release lookup returned HTTP {StatusCode} without a release redirect.", (int)response.StatusCode);
                return null;
            }

            string? version = ReadVersion(target);
            if (version is null)
            {
                logger?.LogDebug("The latest Foundry release tag is not a published release version.");
            }

            return version;
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Offline, rate-limited or timed out: the runtime simply shows no newer release notice.
            logger?.LogDebug(
                "The latest Foundry release could not be read. FailureType={FailureType}",
                exception.GetType().Name);
            return null;
        }
    }

    /// <summary>
    /// Returns the redirect target: the <c>Location</c> header of an unfollowed redirect, or the final request address
    /// of a followed one. A success on the original page means no redirect happened.
    /// </summary>
    private static Uri? ReadTarget(HttpResponseMessage response)
    {
        int statusCode = (int)response.StatusCode;
        if (statusCode is >= 300 and < 400)
        {
            Uri? location = response.Headers.Location;
            return location is null ? null : location.IsAbsoluteUri ? location : new Uri(new Uri(LatestReleasePageUrl), location);
        }

        Uri? finalAddress = response.IsSuccessStatusCode ? response.RequestMessage?.RequestUri : null;
        return finalAddress is null || string.Equals(finalAddress.AbsoluteUri, LatestReleasePageUrl, StringComparison.OrdinalIgnoreCase)
            ? null
            : finalAddress;
    }

    private static string? ReadVersion(Uri target)
    {
        if (target.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(target.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string path = target.AbsolutePath.TrimEnd('/');
        if (!path.StartsWith(ReleaseTagPathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string tag = path[ReleaseTagPathPrefix.Length..];
        if (tag.Length == 0 || tag.Contains('/'))
        {
            return null;
        }

        string value = Uri.UnescapeDataString(tag).Trim();
        if (value.StartsWith('v'))
        {
            value = value[1..];
        }

        return BootMediaFreshnessPolicy.TryParseReleaseVersion(value, out _) ? value : null;
    }
}
