// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Cache;

/// <summary>Names why the network cache share cannot be used, so Deploy can say exactly what to fix.</summary>
public enum NetworkCacheStartupFailureKind
{
    /// <summary>No authored scope contains an address of this device.</summary>
    NoScope,

    /// <summary>The share did not connect or did not answer in time.</summary>
    Unreachable,

    /// <summary>The share refused the configured account.</summary>
    AccessDenied,

    /// <summary>The share is reachable but nothing has been published on it.</summary>
    NotPublished,

    /// <summary>The share is published but a file the deployment needs is missing or does not match the index.</summary>
    ContentMissing
}

/// <summary>
/// Describes one startup failure of the network cache. Holds the share path and account so the message can name them;
/// never holds a password.
/// </summary>
/// <param name="Kind">Why the share cannot be used.</param>
/// <param name="SharePath">UNC path of the share that failed, or <see langword="null"/> when no share was tried.</param>
/// <param name="AccountName">Account configured for that share, or <see langword="null"/> when it is accessed anonymously.</param>
/// <param name="DeviceAddress">
/// Address of the device reported for <see cref="NetworkCacheStartupFailureKind.NoScope"/>: the first non-loopback IPv4 address
/// that is not link-local (169.254/16), else the first non-loopback IPv4 address, else the first non-loopback IPv6 address.
/// </param>
public sealed record NetworkCacheStartupFailure(
    NetworkCacheStartupFailureKind Kind,
    string? SharePath = null,
    string? AccountName = null,
    string? DeviceAddress = null)
{
    /// <summary>
    /// Gets the display names of the published content that is missing or incomplete on the share, in index order, for
    /// <see cref="NetworkCacheStartupFailureKind.ContentMissing"/>; empty for every other kind.
    /// </summary>
    public IReadOnlyList<string> MissingItems { get; init; } = [];

    /// <summary>
    /// Gets whether the share accepted the connection and then refused to read the index or the content, for
    /// <see cref="NetworkCacheStartupFailureKind.AccessDenied"/>. Only the permissions on the server cause that, so a retry
    /// after they are fixed can succeed, unlike a refused logon, which needs other credentials on the boot media.
    /// </summary>
    public bool DeniedAfterConnection { get; init; }

    /// <summary>
    /// Gets whether waiting or fixing the share can resolve the failure without changing the boot media, so Deploy offers a
    /// retry.
    /// </summary>
    public bool CanRetry => Kind.IsRetryable() || (Kind == NetworkCacheStartupFailureKind.AccessDenied && DeniedAfterConnection);
}

/// <summary>Classifies startup failures for the decision to offer a retry.</summary>
public static class NetworkCacheStartupFailureKindExtensions
{
    /// <summary>
    /// Returns whether waiting or fixing the share can resolve every failure of this kind without changing the boot media:
    /// the share may come back, or be published, or be completed. An access denied failure is retryable only when it happened
    /// after the connection; see <see cref="NetworkCacheStartupFailure.CanRetry"/>.
    /// </summary>
    public static bool IsRetryable(this NetworkCacheStartupFailureKind kind) => kind is
        NetworkCacheStartupFailureKind.Unreachable or
        NetworkCacheStartupFailureKind.NotPublished or
        NetworkCacheStartupFailureKind.ContentMissing;
}
