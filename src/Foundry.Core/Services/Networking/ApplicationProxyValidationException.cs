// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Networking;

/// <summary>
/// Identifies why a manual proxy configuration was rejected, so callers can show a localized message.
/// </summary>
public enum ApplicationProxyValidationReason
{
    /// <summary>
    /// The proxy address is missing.
    /// </summary>
    AddressRequired,

    /// <summary>
    /// The proxy address is not a plain HTTP or HTTPS host.
    /// </summary>
    InvalidAddress,

    /// <summary>
    /// The proxy port is outside the 1-65535 range.
    /// </summary>
    InvalidPort
}

/// <summary>
/// Thrown when a manual proxy configuration is invalid. The message is diagnostic text for logs, not user-facing text.
/// </summary>
/// <param name="reason">Why the configuration was rejected.</param>
/// <param name="paramName">The rejected parameter.</param>
public sealed class ApplicationProxyValidationException(ApplicationProxyValidationReason reason, string paramName)
    : ArgumentException($"The manual proxy configuration is invalid. Reason={reason}.", paramName)
{
    /// <summary>
    /// Gets why the configuration was rejected.
    /// </summary>
    public ApplicationProxyValidationReason Reason { get; } = reason;
}
