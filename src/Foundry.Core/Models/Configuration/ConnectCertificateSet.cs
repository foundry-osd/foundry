// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Network;

namespace Foundry.Core.Models.Configuration;

/// <summary>Effective certificates of one runtime 802.1X profile, whatever schema wrote it.</summary>
/// <param name="ClientCertificatePath">The media-relative client certificate path, or <see langword="null"/> when none is staged.</param>
/// <param name="IsClientCertificateContentProtected">Whether the client certificate file is a deployment-keyed <see cref="SecretEnvelope"/> JSON document.</param>
/// <param name="ClientCertificatePasswordSecret">The encrypted client certificate password, or <see langword="null"/> when none is staged.</param>
/// <param name="TrustedRootCertificatePaths">The media-relative trusted root certificate paths, in installation order.</param>
/// <remarks>
/// Schema 6 media describe the certificates with <c>clientCertificate</c> and <c>trustedRootCertificates</c>. Schema 5 media
/// carry one <c>certificatePath</c>, classified by shape with <see cref="NetworkProfileRoamingArtifacts.IsPfxPath"/>: a PFX/P12
/// is the client certificate with its password, any other file is the single trusted root. As soon as a schema 6 member
/// names a file (a client certificate path or a non-blank trusted root), the schema 5 fields are ignored: a profile with trusted
/// roots only drops a legacy PFX and its password, while a client certificate with a blank path counts as absent. Record
/// equality compares <see cref="TrustedRootCertificatePaths"/> by reference.
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
        return Resolve(settings.ClientCertificate, settings.TrustedRootCertificates, settings.CertificatePath, settings.CertificatePfxPasswordSecret);
    }

    /// <summary>Resolves the effective certificates of the Wi-Fi settings.</summary>
    /// <param name="settings">The Wi-Fi runtime settings.</param>
    /// <returns>The effective certificate set, or <see cref="Empty"/> when no certificate is staged.</returns>
    public static ConnectCertificateSet From(ConnectWifiSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return Resolve(settings.ClientCertificate, settings.TrustedRootCertificates, settings.CertificatePath, settings.CertificatePfxPasswordSecret);
    }

    private static ConnectCertificateSet Resolve(
        ConnectClientCertificateSettings? clientCertificate,
        IReadOnlyList<string>? trustedRootCertificates,
        string? legacyCertificatePath,
        SecretEnvelope? legacyPasswordSecret)
    {
        string[] trustedRoots = trustedRootCertificates?.Where(path => !string.IsNullOrWhiteSpace(path)).ToArray() ?? [];
        bool hasClientCertificate = !string.IsNullOrWhiteSpace(clientCertificate?.Path);

        // Any schema 6 member wins: the legacy schema 5 fields are then ignored.
        if (hasClientCertificate || trustedRoots.Length > 0)
        {
            return hasClientCertificate
                ? new(clientCertificate!.Path, clientCertificate.IsContentProtected, clientCertificate.PasswordSecret, trustedRoots)
                : new(null, false, null, trustedRoots);
        }

        if (string.IsNullOrWhiteSpace(legacyCertificatePath))
        {
            return Empty;
        }

        // Schema 5: the single path is the client certificate when it is a PFX, otherwise the only trusted root (its password, if any, is ignored).
        return NetworkProfileRoamingArtifacts.IsPfxPath(legacyCertificatePath)
            ? new(legacyCertificatePath, false, legacyPasswordSecret, [])
            : new(null, false, null, [legacyCertificatePath]);
    }
}
