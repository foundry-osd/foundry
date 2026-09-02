// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net;
using System.Text.RegularExpressions;

namespace Foundry.Core.Services.Networking;

/// <summary>
/// Creates proxy policies used by the Foundry OSD desktop application.
/// </summary>
public static class ApplicationProxyFactory
{
    public static IWebProxy CreateDirect() => new DirectApplicationProxy();

    /// <summary>
    /// Creates a manually configured HTTP proxy.
    /// </summary>
    public static IWebProxy CreateManual(
        string address,
        int port,
        bool bypassLocal,
        string? bypassList,
        ICredentials? credentials)
    {
        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        Uri endpoint = CreateEndpoint(address, port);
        return new ManualApplicationProxy(endpoint, bypassLocal, ParseBypassList(bypassList), credentials);
    }

    private static Uri CreateEndpoint(string address, int port)
    {
        string normalized = address.Trim();
        if (!normalized.Contains("://", StringComparison.Ordinal))
        {
            normalized = $"http://{normalized}";
        }

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out Uri? parsed) ||
            parsed.Scheme is not ("http" or "https") ||
            string.IsNullOrWhiteSpace(parsed.Host) ||
            parsed.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(parsed.Query) ||
            !string.IsNullOrEmpty(parsed.Fragment) ||
            !string.IsNullOrEmpty(parsed.UserInfo))
        {
            throw new ArgumentException("Enter a valid HTTP or HTTPS proxy address.", nameof(address));
        }

        return new UriBuilder(parsed) { Port = port }.Uri;
    }

    private static string[] ParseBypassList(string? bypassList)
    {
        return string.IsNullOrWhiteSpace(bypassList)
            ? []
            : bypassList.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private sealed class ManualApplicationProxy(
        Uri endpoint,
        bool bypassLocal,
        string[] bypassList,
        ICredentials? credentials) : IWebProxy
    {
        public ICredentials? Credentials { get; set; } = credentials;

        public Uri GetProxy(Uri destination) => IsBypassed(destination) ? destination : endpoint;

        public bool IsBypassed(Uri host)
        {
            if (bypassLocal && !host.Host.Contains('.', StringComparison.Ordinal))
            {
                return true;
            }

            return bypassList.Any(pattern => Matches(host.Host, pattern));
        }

        private static bool Matches(string host, string pattern)
        {
            string normalizedPattern = pattern.Trim();
            if (Uri.TryCreate(normalizedPattern, UriKind.Absolute, out Uri? uri))
            {
                normalizedPattern = uri.Host;
            }

            string expression = $"^{Regex.Escape(normalizedPattern).Replace("\\*", ".*", StringComparison.Ordinal)}$";
            return Regex.IsMatch(host, expression, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
    }

    private sealed class DirectApplicationProxy : IWebProxy
    {
        public ICredentials? Credentials { get; set; }

        public Uri GetProxy(Uri destination) => destination;

        public bool IsBypassed(Uri host) => true;
    }
}
