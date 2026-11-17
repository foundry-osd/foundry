// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json.Serialization;

namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Describes, per transport, whether Foundry.Connect captures eligible network profile material and Foundry.Deploy imports it
/// into Windows.
/// </summary>
public sealed record ConnectNetworkProfileRoamingSettings
{
    private readonly NetworkProfileRoamingTransportSettings _wiredDot1x = new();
    private readonly NetworkProfileRoamingTransportSettings _wifi = new();

    /// <summary>
    /// Gets wired 802.1X roaming settings.
    /// </summary>
    public NetworkProfileRoamingTransportSettings WiredDot1x
    {
        get => _wiredDot1x;
        init => _wiredDot1x = value ?? new();
    }

    /// <summary>
    /// Gets Wi-Fi roaming settings.
    /// </summary>
    public NetworkProfileRoamingTransportSettings Wifi
    {
        get => _wifi;
        init => _wifi = value ?? new();
    }

    /// <summary>
    /// Gets whether roaming is enabled for at least one transport.
    /// </summary>
    [JsonIgnore]
    public bool IsAnyEnabled => WiredDot1x.IsEnabled || Wifi.IsEnabled;
}
