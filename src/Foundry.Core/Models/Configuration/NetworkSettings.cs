// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json.Serialization;

namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Describes the network capabilities and profiles to stage on Foundry.Connect media.
/// </summary>
public sealed record NetworkSettings
{
    private bool _roamWiredDot1xProfileToWindows;
    private bool _roamWiredDot1xProfileConfigured;
    private bool _roamWiredDot1xPrivateKeyMaterialToWindows;
    private bool _roamWiredDot1xPrivateKeyMaterialConfigured;
    private bool _roamWifiProfileToWindows;
    private bool _roamWifiProfileConfigured;
    private bool _roamWifiPrivateKeyMaterialToWindows;
    private bool _roamWifiPrivateKeyMaterialConfigured;

    /// <summary>
    /// Gets whether Wi-Fi provisioning should be considered available on the target media.
    /// </summary>
    public bool WifiProvisioned { get; init; }

    /// <summary>
    /// Gets whether Foundry should stage the wired 802.1X profile for import into Windows.
    /// </summary>
    public bool RoamWiredDot1xProfileToWindows
    {
        get => _roamWiredDot1xProfileToWindows;
        init
        {
            _roamWiredDot1xProfileConfigured = true;
            _roamWiredDot1xProfileToWindows = value;
        }
    }

    /// <summary>
    /// Gets whether Foundry should include wired 802.1X PFX/private-key material.
    /// </summary>
    public bool RoamWiredDot1xPrivateKeyMaterialToWindows
    {
        get => RoamWiredDot1xProfileToWindows && _roamWiredDot1xPrivateKeyMaterialToWindows;
        init
        {
            _roamWiredDot1xPrivateKeyMaterialConfigured = true;
            _roamWiredDot1xPrivateKeyMaterialToWindows = value;
        }
    }

    /// <summary>
    /// Gets whether Foundry should stage the Wi-Fi profile for import into Windows.
    /// </summary>
    public bool RoamWifiProfileToWindows
    {
        get => _roamWifiProfileToWindows;
        init
        {
            _roamWifiProfileConfigured = true;
            _roamWifiProfileToWindows = value;
        }
    }

    /// <summary>
    /// Gets whether Foundry should include Wi-Fi PFX/private-key material.
    /// </summary>
    public bool RoamWifiPrivateKeyMaterialToWindows
    {
        get => RoamWifiProfileToWindows && _roamWifiPrivateKeyMaterialToWindows;
        init
        {
            _roamWifiPrivateKeyMaterialConfigured = true;
            _roamWifiPrivateKeyMaterialToWindows = value;
        }
    }

    /// <summary>
    /// Migrates the legacy shared profile roaming opt-in to both transports.
    /// </summary>
    [JsonPropertyName("roamWifiProfilesToWindows")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LegacyRoamNetworkProfilesToWindows
    {
        get => null;
        init
        {
            if (value is true)
            {
                if (!_roamWiredDot1xProfileConfigured)
                {
                    _roamWiredDot1xProfileToWindows = true;
                }

                if (!_roamWifiProfileConfigured)
                {
                    _roamWifiProfileToWindows = true;
                }
            }
        }
    }

    /// <summary>
    /// Migrates the legacy shared private-key opt-in to both transports.
    /// </summary>
    [JsonPropertyName("roamPrivateKeyMaterialToWindows")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LegacyRoamPrivateKeyMaterialToWindows
    {
        get => null;
        init
        {
            if (value is true)
            {
                if (!_roamWiredDot1xPrivateKeyMaterialConfigured)
                {
                    _roamWiredDot1xPrivateKeyMaterialToWindows = true;
                }

                if (!_roamWifiPrivateKeyMaterialConfigured)
                {
                    _roamWifiPrivateKeyMaterialToWindows = true;
                }
            }
        }
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
