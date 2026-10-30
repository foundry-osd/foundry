// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Network;

namespace Foundry.Core.Services.Configuration;

/// <summary>
/// Classifies authoring certificates by shape: a PFX/P12 package is the client certificate, any other file is a trusted root.
/// </summary>
/// <remarks>
/// The shape rule is <see cref="NetworkProfileRoamingArtifacts.IsPfxPath"/>, the one PFX rule shared with Foundry.Connect. It
/// moves the single certificate of older configurations into the separate fields and imports legacy certificate paths.
/// </remarks>
public static class NetworkCertificateShapes
{
    /// <summary>Returns whether <paramref name="reference"/> is a client certificate (<c>.pfx</c> or <c>.p12</c>, case-insensitive).</summary>
    public static bool IsClientCertificate(AuthoringFileReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return NetworkProfileRoamingArtifacts.IsPfxPath(reference.FileName);
    }

    /// <summary>Returns whether wired 802.1X has a client certificate or at least one trusted root.</summary>
    public static bool HasCertificates(Dot1xSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.ClientCertificate is not null || settings.TrustedRootCertificates.Count > 0;
    }

    /// <summary>Returns whether Wi-Fi has a client certificate or at least one trusted root.</summary>
    public static bool HasCertificates(WifiSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.ClientCertificate is not null || settings.TrustedRootCertificates.Count > 0;
    }

    /// <summary>Returns every wired 802.1X certificate: the client certificate first, then the trusted roots in order.</summary>
    public static IEnumerable<AuthoringFileReference> All(Dot1xSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return Concat(settings.ClientCertificate, settings.TrustedRootCertificates);
    }

    /// <summary>Returns every Wi-Fi certificate: the client certificate first, then the trusted roots in order.</summary>
    public static IEnumerable<AuthoringFileReference> All(WifiSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return Concat(settings.ClientCertificate, settings.TrustedRootCertificates);
    }

    /// <summary>
    /// Adds <paramref name="reference"/> by shape: a PFX fills <see cref="Dot1xSettings.ClientCertificate"/> when it is empty; any
    /// other file is appended to the trusted roots unless its content hash is already there. A null reference changes nothing.
    /// </summary>
    public static Dot1xSettings WithCertificate(Dot1xSettings settings, AuthoringFileReference? reference)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (reference is null) return settings;
        if (IsClientCertificate(reference)) return settings.ClientCertificate is null ? settings with { ClientCertificate = reference } : settings;
        return settings with { TrustedRootCertificates = WithRoot(settings.TrustedRootCertificates, reference) };
    }

    /// <summary>
    /// Adds <paramref name="reference"/> by shape: a PFX fills <see cref="WifiSettings.ClientCertificate"/> when it is empty; any
    /// other file is appended to the trusted roots unless its content hash is already there. A null reference changes nothing.
    /// </summary>
    public static WifiSettings WithCertificate(WifiSettings settings, AuthoringFileReference? reference)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (reference is null) return settings;
        if (IsClientCertificate(reference)) return settings.ClientCertificate is null ? settings with { ClientCertificate = reference } : settings;
        return settings with { TrustedRootCertificates = WithRoot(settings.TrustedRootCertificates, reference) };
    }

    private static IEnumerable<AuthoringFileReference> Concat(AuthoringFileReference? client, IReadOnlyList<AuthoringFileReference> roots) =>
        client is null ? roots : roots.Prepend(client);

    private static IReadOnlyList<AuthoringFileReference> WithRoot(IReadOnlyList<AuthoringFileReference> roots, AuthoringFileReference reference) =>
        roots.Any(root => string.Equals(root.ContentHash, reference.ContentHash, StringComparison.OrdinalIgnoreCase))
            ? roots
            : [.. roots, reference];
}
