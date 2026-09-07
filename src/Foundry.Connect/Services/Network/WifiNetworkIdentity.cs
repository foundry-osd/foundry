// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Models.Network;
using Foundry.Core.Services.Configuration;
using System.Text;

namespace Foundry.Connect.Services.Network;

internal static class WifiNetworkIdentity
{
    public static string? GetIdentity(string? ssid, string? ssidHex)
    {
        try { return WifiProfileXmlBuilder.GetSsidHex(ssid, ssidHex); }
        catch (ArgumentException) { return null; }
    }

    public static bool Matches(string? firstSsid, string? firstHex, string? secondSsid, string? secondHex)
    {
        string? first = GetIdentity(firstSsid, firstHex);
        return first is not null && first == GetIdentity(secondSsid, secondHex);
    }

    public static IReadOnlyList<WifiNetworkSummary> SelectStrongest(IEnumerable<WifiNetworkSummary> networks)
    {
        var selected = new Dictionary<string, WifiNetworkSummary>(StringComparer.Ordinal);
        foreach (WifiNetworkSummary network in networks)
        {
            string? identity = GetIdentity(network.Ssid, network.SsidHex);
            if (identity is not null && (!selected.TryGetValue(identity, out var existing) ||
                network.SignalStrengthPercent > existing.SignalStrengthPercent))
                selected[identity] = network;
        }
        return selected.Values.OrderByDescending(network => network.SignalStrengthPercent)
            .ThenBy(network => GetIdentity(network.Ssid, network.SsidHex), StringComparer.Ordinal).ToArray();
    }

    internal static string DecodeSsid(byte[] bytes, uint length)
    {
        return length is > 0 and <= 32 && length <= bytes.Length
            ? Encoding.UTF8.GetString(bytes, 0, (int)length) : "Hidden network";
    }
}
