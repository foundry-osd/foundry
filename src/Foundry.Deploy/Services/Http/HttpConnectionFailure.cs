// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net.Http;

namespace Foundry.Deploy.Services.Http;

/// <summary>
/// Identifies TLS failures without depending on platform-specific exception text.
/// </summary>
internal static class HttpConnectionFailure
{
    /// <summary>
    /// Invariant guidance mapped to localized text by the Deploy shell. Original exceptions remain in logs.
    /// </summary>
    internal const string SecureConnectionMessage = "A secure connection could not be established. Check the device date and time and the trusted certificates in your boot media, including any HTTPS proxy certificate.";

    /// <summary>
    /// Indicates a TLS handshake failure that must not be retried or treated as an unavailable optional catalog.
    /// </summary>
    internal static bool IsSecureConnectionFailure(Exception exception)
    {
        return exception is HttpRequestException { HttpRequestError: HttpRequestError.SecureConnectionError };
    }

    /// <summary>
    /// Supplies actionable TLS guidance while retaining existing messages for other failures.
    /// </summary>
    internal static string GetMessage(Exception exception)
    {
        return IsSecureConnectionFailure(exception) ? SecureConnectionMessage : exception.Message;
    }
}
