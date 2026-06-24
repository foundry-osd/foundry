// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Describes the network capabilities and profiles to stage on Foundry.Connect media.
/// </summary>
public sealed record NetworkSettings
{
    private readonly bool _roamPrivateKeyMaterialToWindows;

    /// <summary>
    /// Gets whether Wi-Fi provisioning should be considered available on the target media.
    /// </summary>
    public bool WifiProvisioned { get; init; }

    /// <summary>
    /// Gets whether Foundry should stage eligible WinPE network profiles for import into the deployed Windows installation.
    /// </summary>
    public bool RoamWifiProfilesToWindows { get; init; }

    /// <summary>
    /// Gets whether Foundry should securely roam explicitly configured 802.1X PFX/private-key material to the deployed Windows installation.
    /// </summary>
    public bool RoamPrivateKeyMaterialToWindows
    {
        get => RoamWifiProfilesToWindows && _roamPrivateKeyMaterialToWindows;
        init => _roamPrivateKeyMaterialToWindows = value;
    }

    /// <summary>
    /// Gets wired 802.1X provisioning settings.
    /// </summary>
    public Dot1xSettings Dot1x { get; init; } = new();

    /// <summary>
    /// Gets Wi-Fi provisioning settings.
    /// </summary>
    public WifiSettings Wifi { get; init; } = new();
}
