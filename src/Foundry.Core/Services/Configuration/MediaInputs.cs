// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Text;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.NetworkCache;

namespace Foundry.Core.Services.Configuration;

/// <summary>
/// Values that validation and media generation need beside the authoring document: where each referenced authoring file
/// can be read, and the passwords that never enter the document.
/// </summary>
/// <remarks>
/// Pages resolve references to the authoring file library; a media build resolves them to its private verified copies.
/// </remarks>
public sealed record MediaInputs
{
    /// <summary>Gets inputs that resolve no file and carry no password.</summary>
    public static MediaInputs None { get; } = new();

    /// <summary>Gets the function returning a readable local path for a reference, or <see langword="null"/> when the file is unavailable.</summary>
    public Func<AuthoringFileReference, string?> ResolveFile { get; init; } = static _ => null;

    /// <summary>Gets the network passwords for the current network settings.</summary>
    public NetworkSecrets Network { get; init; } = new();

    /// <summary>Gets the password of the Autopilot boot media PFX.</summary>
    public string? AutopilotPfxPassword { get; init; }

    /// <summary>Gets the network cache account passwords, keyed by account id.</summary>
    public IReadOnlyDictionary<string, string> NetworkCachePasswords { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Gets the signed content index binding. Required when the network cache is active, because the boot configuration then
    /// carries the publisher key instead of the imported content.
    /// </summary>
    public NetworkCacheIndexBinding? NetworkCacheIndex { get; init; }

    /// <summary>Resolves an optional reference; a reference that cannot be resolved throws.</summary>
    /// <exception cref="FileNotFoundException">The referenced file is unavailable.</exception>
    internal string? ResolveRequired(AuthoringFileReference? reference) => reference is null
        ? null
        : ResolveFile(reference) ?? throw new FileNotFoundException("A referenced authoring file is unavailable. Select the file again.");

    // Records print their members; a password is reported as present or absent, never printed.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Network = ").Append(Network).Append(", HasAutopilotPfxPassword = ").Append(AutopilotPfxPassword is not null)
            .Append(", NetworkCachePasswordCount = ").Append(NetworkCachePasswords.Count)
            .Append(", NetworkCacheIndexMinimumSequence = ").Append(NetworkCacheIndex?.MinimumSequence.ToString(CultureInfo.InvariantCulture) ?? "none");
        return true;
    }
}

/// <summary>
/// Network passwords kept outside the authoring document.
/// </summary>
public sealed record NetworkSecrets
{
    /// <summary>Gets the personal Wi-Fi passphrase.</summary>
    public string? WifiPassphrase { get; init; }

    /// <summary>Gets the PFX password of the wired 802.1X certificate.</summary>
    public string? WiredCertificatePfxPassword { get; init; }

    /// <summary>Gets the PFX password of the Wi-Fi enterprise certificate.</summary>
    public string? WifiCertificatePfxPassword { get; init; }

    // Records print their members; a password is reported as present or absent, never printed.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("HasWifiPassphrase = ").Append(WifiPassphrase is not null)
            .Append(", HasWiredCertificatePfxPassword = ").Append(WiredCertificatePfxPassword is not null)
            .Append(", HasWifiCertificatePfxPassword = ").Append(WifiCertificatePfxPassword is not null);
        return true;
    }
}
