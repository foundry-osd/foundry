// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;
using Foundry.Connect.Models;
using Foundry.Connect.Models.Network;

namespace Foundry.Connect.Services.Network;

/// <summary>
/// Converts runtime network details into stable telemetry categories.
/// </summary>
internal static class NetworkTelemetryClassifier
{
    /// <summary>
    /// Classifies a network exception without retaining messages, addresses, or request data.
    /// </summary>
    /// <param name="exception">Network operation exception.</param>
    /// <param name="cancellationRequested">Whether the caller requested cancellation.</param>
    /// <returns>Stable low-cardinality failure fields.</returns>
    public static NetworkFailureClassification ClassifyFailure(Exception exception, bool cancellationRequested = false)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is OperationCanceledException)
        {
            return new NetworkFailureClassification(
                "network",
                cancellationRequested ? "cancelled" : "timeout");
        }

        if (exception is HttpRequestException { StatusCode: HttpStatusCode.ProxyAuthenticationRequired })
        {
            return new NetworkFailureClassification("network", "proxy", "407");
        }

        if (exception is HttpRequestException { StatusCode: HttpStatusCode statusCode })
        {
            return new NetworkFailureClassification(
                "network",
                "http_status",
                ((int)statusCode).ToString(CultureInfo.InvariantCulture));
        }

        if (FindInnerException<AuthenticationException>(exception) is not null)
        {
            return new NetworkFailureClassification("network", "tls");
        }

        if (FindInnerException<SocketException>(exception) is SocketException socketException &&
            socketException.SocketErrorCode is SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain)
        {
            return new NetworkFailureClassification("network", "dns", socketException.SocketErrorCode.ToString());
        }

        return new NetworkFailureClassification("network", "transport");
    }

    /// <summary>
    /// Classifies the active connection type without exposing adapter or network identifiers.
    /// </summary>
    /// <param name="snapshot">Current network status snapshot.</param>
    /// <returns>A stable connection category.</returns>
    public static string ClassifyConnection(NetworkStatusSnapshot snapshot)
    {
        if (snapshot.IsEthernetConnected)
        {
            return "ethernet";
        }

        return string.IsNullOrWhiteSpace(snapshot.ConnectedWifiSsid) ? "unknown" : "wifi";
    }

    /// <summary>
    /// Classifies the runtime layout into a stable telemetry category.
    /// </summary>
    /// <param name="layoutMode">Detected Connect layout mode.</param>
    /// <returns>A stable layout category.</returns>
    public static string ClassifyLayout(NetworkLayoutMode layoutMode)
    {
        return layoutMode switch
        {
            NetworkLayoutMode.EthernetWifi => "ethernet_wifi",
            _ => "ethernet_only"
        };
    }

    /// <summary>
    /// Classifies a native WLAN authentication label into a low-cardinality security category.
    /// </summary>
    /// <param name="authentication">Native authentication label.</param>
    /// <returns>A stable Wi-Fi security category.</returns>
    public static string ClassifyWifiSecurity(string? authentication)
    {
        if (string.IsNullOrWhiteSpace(authentication))
        {
            return "none";
        }

        string normalized = authentication.Trim();
        if (normalized.Contains("OWE", StringComparison.OrdinalIgnoreCase))
        {
            return "owe";
        }

        if (normalized.Contains("Open", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            return "open";
        }

        if (normalized.Contains("Enterprise", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("802.1X", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("EAP", StringComparison.OrdinalIgnoreCase))
        {
            return "enterprise";
        }

        if (normalized.Contains("Personal", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("PSK", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("SAE", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("WEP", StringComparison.OrdinalIgnoreCase))
        {
            return "personal";
        }

        return "unknown";
    }

    private static TException? FindInnerException<TException>(Exception exception)
        where TException : Exception
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is TException match)
            {
                return match;
            }
        }

        return null;
    }
}

/// <summary>
/// Contains privacy-safe network failure fields for remote diagnostics.
/// </summary>
internal sealed record NetworkFailureClassification(string Kind, string Reason, string? Code = null);
