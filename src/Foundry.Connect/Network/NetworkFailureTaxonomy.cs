// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;

namespace Foundry.Connect.Network;

/// <summary>
/// Privacy-safe network failure fields for remote diagnostics.
/// </summary>
/// <param name="Kind">Failure family; always <c>network</c> for network operations.</param>
/// <param name="Reason">Stable low-cardinality reason.</param>
/// <param name="Code">Stable low-cardinality code, when one exists.</param>
public sealed record NetworkFailureDescriptor(string Kind, string Reason, string? Code);

/// <summary>
/// Maps typed network failures and exceptions to remote diagnostic fields without retaining messages, addresses,
/// SSIDs or request data. Values keep the taxonomy already used by Connect.
/// </summary>
public static class NetworkFailureTaxonomy
{
    private const string NetworkKind = "network";

    /// <summary>
    /// Describes a failure code.
    /// </summary>
    /// <param name="code">Failure code.</param>
    /// <returns>Remote diagnostic fields.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The code is <see cref="NetworkFailureCode.None"/> or undefined.</exception>
    public static NetworkFailureDescriptor Describe(NetworkFailureCode code)
    {
        (string reason, string failureCode) = code switch
        {
            NetworkFailureCode.Unexpected => ("unexpected", "network_unexpected"),
            NetworkFailureCode.WifiFeatureDisabled or NetworkFailureCode.ProvisionedProfileUnavailable => ("profile_unavailable", "wifi_profile_unavailable"),
            NetworkFailureCode.WifiRuntimeMissing => ("missing_runtime", "wifi_runtime_missing"),
            NetworkFailureCode.NoWirelessAdapter => ("missing_adapter", "no_wireless_adapter"),
            NetworkFailureCode.WifiSecretUnavailable => ("secret_unavailable", "wifi_secret_unavailable"),
            NetworkFailureCode.InvalidPassphrase => ("invalid_input", "wifi_passphrase_invalid"),
            NetworkFailureCode.NetworkNotVisible => ("invalid_input", "wifi_network_not_visible"),
            NetworkFailureCode.UnsupportedSecurity => ("unsupported", "wifi_runtime_not_supported"),
            NetworkFailureCode.WifiProfileRejected => ("profile_import_failed", "wifi_profile_import_failed"),
            NetworkFailureCode.ConnectRequestFailed => ("connect_request_failed", "wifi_connect_request_failed"),
            NetworkFailureCode.ConnectionFailed => ("connect_failed", "wifi_connect_failed"),
            NetworkFailureCode.ConnectTimedOut => ("timeout", "wifi_connect_timeout"),
            NetworkFailureCode.DisconnectFailed => ("disconnect_request_failed", "wifi_disconnect_request_failed"),
            NetworkFailureCode.DisconnectTimedOut => ("timeout", "wifi_disconnect_timeout"),
            NetworkFailureCode.WifiCertificateImportFailed => ("certificate_import_failed", "wifi_certificate_import_failed"),
            NetworkFailureCode.WiredProfileTemplateMissing => ("profile_unavailable", "wired_profile_template_missing"),
            NetworkFailureCode.WiredSecretUnavailable => ("secret_unavailable", "wired_secret_unavailable"),
            NetworkFailureCode.WiredCertificateImportFailed => ("certificate_import_failed", "wired_certificate_import_failed"),
            NetworkFailureCode.WiredProfileImportFailed => ("profile_import_failed", "wired_profile_import_failed"),
            NetworkFailureCode.WiredReconnectFailed => ("reconnect_request_failed", "wired_reconnect_request_failed"),
            _ => throw new ArgumentOutOfRangeException(nameof(code), code, "The failure code has no remote description.")
        };

        return new NetworkFailureDescriptor(NetworkKind, reason, failureCode);
    }

    /// <summary>
    /// Classifies a network exception without retaining messages, addresses, or request data.
    /// </summary>
    /// <param name="exception">Network operation exception.</param>
    /// <param name="cancellationRequested">Whether the caller requested cancellation.</param>
    /// <returns>Stable low-cardinality failure fields.</returns>
    public static NetworkFailureDescriptor ClassifyException(Exception exception, bool cancellationRequested = false)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is OperationCanceledException)
        {
            return new NetworkFailureDescriptor(NetworkKind, cancellationRequested ? "cancelled" : "timeout", null);
        }

        if (exception is HttpRequestException { StatusCode: HttpStatusCode.ProxyAuthenticationRequired })
        {
            return new NetworkFailureDescriptor(NetworkKind, "proxy", "407");
        }

        if (exception is HttpRequestException { StatusCode: HttpStatusCode statusCode })
        {
            return new NetworkFailureDescriptor(NetworkKind, "http_status", ((int)statusCode).ToString(CultureInfo.InvariantCulture));
        }

        if (FindInnerException<AuthenticationException>(exception) is not null)
        {
            return new NetworkFailureDescriptor(NetworkKind, "tls", null);
        }

        if (FindInnerException<SocketException>(exception) is SocketException socketException &&
            socketException.SocketErrorCode is SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain)
        {
            return new NetworkFailureDescriptor(NetworkKind, "dns", socketException.SocketErrorCode.ToString());
        }

        return new NetworkFailureDescriptor(NetworkKind, "transport", null);
    }

    /// <summary>
    /// Gets the numeric code logged next to the exception type in place of the exception itself: the Win32 error of a
    /// <see cref="Win32Exception"/>, otherwise <see cref="Exception.HResult"/>.
    /// </summary>
    /// <remarks>
    /// The remote Logs stream mirrors every log property and the message of an attached exception, and exception
    /// messages can carry a file path, an SSID or a profile name, so network log events carry the type and this code only.
    /// </remarks>
    /// <param name="exception">The exception.</param>
    /// <returns>The Win32 error or the HRESULT.</returns>
    public static int ExceptionErrorCode(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception is Win32Exception win32 ? win32.NativeErrorCode : exception.HResult;
    }

    /// <summary>
    /// Gets the stable remote <c>NetworkOperation</c> name of an operation.
    /// </summary>
    /// <param name="operation">Operation kind.</param>
    /// <returns>The operation name.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The operation is undefined.</exception>
    public static string OperationName(NetworkOperationKind operation)
    {
        return operation switch
        {
            NetworkOperationKind.ApplyProvisioning => "network.apply_provisioned_settings",
            NetworkOperationKind.ConnectProvisionedWifi => "wifi.provisioned_action",
            NetworkOperationKind.ConnectWifiNetwork => "wifi.selected_action",
            NetworkOperationKind.DisconnectWifi => "wifi.disconnect_action",
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "The operation has no remote name.")
        };
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
