// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration;

/// <summary>Effective certificates of one runtime 802.1X profile, read the same way for wired 802.1X and enterprise Wi-Fi.</summary>
/// <param name="ClientCertificatePath">The media-relative client certificate path, or <see langword="null"/> when none is staged.</param>
/// <param name="IsClientCertificateContentProtected">Whether the client certificate file is a deployment-keyed <see cref="SecretEnvelope"/> JSON document.</param>
/// <param name="ClientCertificatePasswordSecret">The encrypted client certificate password, or <see langword="null"/> when none is staged.</param>
/// <param name="TrustedRootCertificatePaths">The media-relative trusted root certificate paths, in installation order.</param>
/// <remarks>
/// A client certificate with a blank path counts as absent, and blank trusted root paths are dropped, so a hand-edited file
/// never makes Connect install an empty path. Record equality compares <see cref="TrustedRootCertificatePaths"/> by reference.
/// </remarks>
public sealed record ConnectCertificateSet(
    string? ClientCertificatePath,
    bool IsClientCertificateContentProtected,
    SecretEnvelope? ClientCertificatePasswordSecret,
    IReadOnlyList<string> TrustedRootCertificatePaths)
{
    /// <summary>Gets the set of a profile that stages no certificate.</summary>
    public static ConnectCertificateSet Empty { get; } = new(null, false, null, []);

    /// <summary>Resolves the effective certificates of the wired 802.1X settings.</summary>
    /// <param name="settings">The wired 802.1X runtime settings.</param>
    /// <returns>The effective certificate set, or <see cref="Empty"/> when no certificate is staged.</returns>
    public static ConnectCertificateSet From(ConnectDot1xSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return Resolve(settings.ClientCertificate, settings.TrustedRootCertificates);
    }

    /// <summary>Resolves the effective certificates of the Wi-Fi settings.</summary>
    /// <param name="settings">The Wi-Fi runtime settings.</param>
    /// <returns>The effective certificate set, or <see cref="Empty"/> when no certificate is staged.</returns>
    public static ConnectCertificateSet From(ConnectWifiSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return Resolve(settings.ClientCertificate, settings.TrustedRootCertificates);
    }

    private static ConnectCertificateSet Resolve(
        ConnectClientCertificateSettings? clientCertificate,
        IReadOnlyList<string>? trustedRootCertificates)
    {
        string[] trustedRoots = trustedRootCertificates?.Where(path => !string.IsNullOrWhiteSpace(path)).ToArray() ?? [];
        if (string.IsNullOrWhiteSpace(clientCertificate?.Path))
        {
            return trustedRoots.Length == 0 ? Empty : new(null, false, null, trustedRoots);
        }

        return new(clientCertificate.Path, clientCertificate.IsContentProtected, clientCertificate.PasswordSecret, trustedRoots);
    }
}
