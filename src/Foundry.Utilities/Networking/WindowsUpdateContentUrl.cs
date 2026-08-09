// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Utilities.Networking;

/// <summary>
/// Normalizes Windows Update content delivery URLs for environments that require HTTP downloads.
/// </summary>
public static class WindowsUpdateContentUrl
{
    private const string ContentHost = "dl.delivery.mp.microsoft.com";

    /// <summary>
    /// Converts HTTPS URLs for the Windows Update content host and its subdomains to HTTP.
    /// </summary>
    public static string Normalize(string sourceUrl)
    {
        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out Uri? uri))
        {
            return sourceUrl;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !IsContentHost(uri.Host))
        {
            return sourceUrl;
        }

        var builder = new UriBuilder(uri)
        {
            Scheme = Uri.UriSchemeHttp,
            Port = uri.Port == 443 ? 80 : uri.Port
        };

        return builder.Uri.AbsoluteUri;
    }

    private static bool IsContentHost(string host)
    {
        return host.Equals(ContentHost, StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith($".{ContentHost}", StringComparison.OrdinalIgnoreCase);
    }
}
