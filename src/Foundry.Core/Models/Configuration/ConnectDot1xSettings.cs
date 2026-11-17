// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Describes the wired 802.1X settings written to the Foundry.Connect section of the WinPE runtime configuration.
/// </summary>
/// <remarks>
/// This is part of the <c>connect</c> section of the WinPE runtime configuration and is kept separate from the authoring <see cref="Dot1xSettings"/>,
/// so authoring changes cannot alter the generated JSON. Paths are media-relative and no plaintext secret is carried.
/// </remarks>
public sealed record ConnectDot1xSettings
{
    /// <summary>
    /// Gets whether wired 802.1X provisioning is enabled.
    /// </summary>
    public bool IsEnabled { get; init; }

    /// <summary>
    /// Gets the media-relative path of the staged XML profile.
    /// </summary>
    public string? ProfileTemplatePath { get; init; }

    /// <summary>
    /// Gets the client certificate staged with the profile, or <see langword="null"/> when none is staged.
    /// </summary>
    public ConnectClientCertificateSettings? ClientCertificate { get; init; }

    /// <summary>
    /// Gets the media-relative paths of the trusted root certificates (<c>.cer</c>/<c>.crt</c>) staged with the profile,
    /// in installation order, or <see langword="null"/> when none is staged.
    /// </summary>
    public IReadOnlyList<string>? TrustedRootCertificates { get; init; }
}
