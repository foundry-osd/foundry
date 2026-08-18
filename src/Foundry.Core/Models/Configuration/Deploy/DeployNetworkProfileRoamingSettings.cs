// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json.Serialization;

namespace Foundry.Core.Models.Configuration.Deploy;

/// <summary>
/// Describes whether Foundry.Deploy should import eligible network profile material before OOBE.
/// </summary>
public sealed record DeployNetworkProfileRoamingSettings
{
    private NetworkProfileRoamingTransportSettings _wiredDot1x = new();
    private NetworkProfileRoamingTransportSettings _wifi = new();
    private bool? _legacyIsEnabled;
    private bool? _legacyIncludePrivateKeyMaterial;

    /// <summary>
    /// Gets wired 802.1X roaming settings.
    /// </summary>
    public NetworkProfileRoamingTransportSettings WiredDot1x
    {
        get => _wiredDot1x;
        init
        {
            _wiredDot1x = value ?? new();
            ApplyLegacySettings();
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
            _wifi = value ?? new();
            ApplyLegacySettings();
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
            _legacyIsEnabled = value;
            ApplyLegacySettings();
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
            _legacyIncludePrivateKeyMaterial = value;
            ApplyLegacySettings();
        }
    }

    private void ApplyLegacySettings()
    {
        _wiredDot1x = NetworkProfileRoamingLegacyMigration.Apply(
            _wiredDot1x,
            _legacyIsEnabled,
            _legacyIncludePrivateKeyMaterial);
        _wifi = NetworkProfileRoamingLegacyMigration.Apply(
            _wifi,
            _legacyIsEnabled,
            _legacyIncludePrivateKeyMaterial);
    }

    /// <summary>
    /// Gets the artifact root path consumed by Foundry.Deploy inside WinPE.
    /// </summary>
    public string ArtifactRootPath { get; init; } = Foundry.Core.Models.Network.NetworkProfileRoamingArtifacts.DefaultArtifactRootPath;
}
