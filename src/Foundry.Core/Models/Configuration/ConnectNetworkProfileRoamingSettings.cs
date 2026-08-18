// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json.Serialization;

namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Describes whether Foundry.Connect should capture eligible network profile material for Windows import.
/// </summary>
public sealed record ConnectNetworkProfileRoamingSettings
{
    private NetworkProfileRoamingTransportSettings _wiredDot1x = new();
    private bool _wiredDot1xConfigured;
    private NetworkProfileRoamingTransportSettings _wifi = new();
    private bool _wifiConfigured;

    /// <summary>
    /// Gets wired 802.1X roaming settings.
    /// </summary>
    public NetworkProfileRoamingTransportSettings WiredDot1x
    {
        get => _wiredDot1x;
        init
        {
            _wiredDot1xConfigured = true;
            _wiredDot1x = value ?? new();
        }
    }

    /// <summary>
    /// Gets Wi-Fi roaming settings.
    /// </summary>
    public NetworkProfileRoamingTransportSettings Wifi
    {
        get => _wifi;
        init
        {
            _wifiConfigured = true;
            _wifi = value ?? new();
        }
    }

    /// <summary>
    /// Gets whether roaming is enabled for at least one transport.
    /// </summary>
    [JsonIgnore]
    public bool IsAnyEnabled => WiredDot1x.IsEnabled || Wifi.IsEnabled;

    /// <summary>
    /// Migrates the legacy shared runtime opt-in to both transports.
    /// </summary>
    [JsonPropertyName("isEnabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LegacyIsEnabled
    {
        get => null;
        init
        {
            if (value is true)
            {
                if (!_wiredDot1xConfigured)
                {
                    _wiredDot1x = _wiredDot1x with { IsEnabled = true };
                }

                if (!_wifiConfigured)
                {
                    _wifi = _wifi with { IsEnabled = true };
                }
            }
        }
    }

    /// <summary>
    /// Migrates the legacy shared private-key opt-in to both transports.
    /// </summary>
    [JsonPropertyName("includePrivateKeyMaterial")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LegacyIncludePrivateKeyMaterial
    {
        get => null;
        init
        {
            if (value is true)
            {
                if (!_wiredDot1xConfigured)
                {
                    _wiredDot1x = _wiredDot1x with { IncludePrivateKeyMaterial = true };
                }

                if (!_wifiConfigured)
                {
                    _wifi = _wifi with { IncludePrivateKeyMaterial = true };
                }
            }
        }
    }
}
