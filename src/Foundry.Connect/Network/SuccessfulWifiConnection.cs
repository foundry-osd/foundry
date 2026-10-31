// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Text;

namespace Foundry.Connect.Network;

/// <summary>
/// A Wi-Fi connection that succeeded during this session, kept so the exit step can decide what to roam.
/// </summary>
/// <remarks>
/// The SSID and the profile name identify a network and must never be logged, so the generated
/// <see cref="ToString"/> omits them.
/// </remarks>
/// <param name="InterfaceId">WLAN interface that connected; WLAN profiles are per interface.</param>
/// <param name="ProfileName">WLAN profile name used for the connection.</param>
/// <param name="Ssid">Connected SSID, or <see langword="null"/> for a provisioned profile that states none.</param>
/// <param name="Source">Whether the profile came from the media or from the technician.</param>
/// <param name="LastSucceededAt">Time of the last successful connection with this profile on this interface.</param>
public sealed record SuccessfulWifiConnection(
    Guid InterfaceId,
    string ProfileName,
    WifiSsid? Ssid,
    WifiConnectionSource Source,
    DateTimeOffset LastSucceededAt)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("InterfaceId = ").Append(InterfaceId)
            .Append(", Source = ").Append(Source)
            .Append(", LastSucceededAt = ").Append(LastSucceededAt.ToString("O", CultureInfo.InvariantCulture));
        return true;
    }
}
