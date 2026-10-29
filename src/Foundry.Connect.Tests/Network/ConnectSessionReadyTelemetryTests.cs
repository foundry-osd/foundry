// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Network;
using Foundry.Connect.Telemetry;
using Foundry.Core.Models.Configuration;
using Foundry.Telemetry;

namespace Foundry.Connect.Tests.Network;

public sealed class ConnectSessionReadyTelemetryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Build_OnProvisionedWifiPath_ReportsWifiFields()
    {
        NetworkState state = NetworkState.Initial(Now) with
        {
            Internet = InternetStatus.Online,
            Wifi = WifiState.Initial with
            {
                Availability = WifiAvailability.Available,
                Connection = new WifiConnection(Guid.NewGuid(), WifiSsid.FromText("Lab"), "Lab", WifiSecurityType.Wpa3Personal, WifiConnectionSource.Provisioned)
            }
        };
        var configuration = new FoundryConnectConfigurationDocument
        {
            Capabilities = new ConnectNetworkCapabilitiesSettings { WifiProvisioned = true },
            Wifi = new ConnectWifiSettings { IsEnabled = true }
        };

        IReadOnlyDictionary<string, object?> properties = ConnectSessionReadyTelemetry.Build(state, configuration);

        Assert.Equal("wifi", properties["connect_network_connection_type"]);
        Assert.False(properties.ContainsKey("connect_network_layout_mode"));
        Assert.Equal(false, properties["connect_ethernet_available"]);
        Assert.Equal(true, properties["connect_wifi_available"]);
        Assert.Equal("personal", properties["connect_wifi_security_type"]);
        Assert.Equal("provisioned", properties["connect_wifi_source"]);
        Assert.Equal(false, properties["connect_wired_dot1x_enabled"]);
        Assert.Equal(true, properties["connect_wifi_provisioned"]);
        Assert.DoesNotContain(properties.Values, static value => value is string text && text.Contains("Lab", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_OnEthernetPath_ReportsNoneForWifiFields()
    {
        NetworkState state = NetworkState.Initial(Now) with
        {
            Internet = InternetStatus.Online,
            Ethernet = new EthernetState([new EthernetInterfaceState("eth0", "Ethernet", true, IpConfigurationKind.Dhcp, "192.0.2.10", "192.0.2.1")], Dot1xState.NotConfigured),
            Wifi = WifiState.Initial with { Availability = WifiAvailability.RuntimeMissing }
        };

        IReadOnlyDictionary<string, object?> properties = ConnectSessionReadyTelemetry.Build(state, new FoundryConnectConfigurationDocument());

        Assert.Equal("ethernet", properties["connect_network_connection_type"]);
        Assert.False(properties.ContainsKey("connect_network_layout_mode"));
        Assert.Equal("none", properties["connect_wifi_security_type"]);
        Assert.Equal("none", properties["connect_wifi_source"]);
    }

    [Fact]
    public void Build_SendsExactlyTheSinglePageProperties()
    {
        NetworkState state = NetworkState.Initial(Now) with { Internet = InternetStatus.Online };

        IReadOnlyDictionary<string, object?> properties = ConnectSessionReadyTelemetry.Build(state, new FoundryConnectConfigurationDocument());

        Assert.Equal(
            ["connect_ethernet_available", "connect_network_connection_type", "connect_wifi_available", "connect_wifi_provisioned", "connect_wifi_security_type", "connect_wifi_source", "connect_wired_dot1x_enabled"],
            properties.Keys.Order(StringComparer.Ordinal));
    }

    // Contract check: everything Connect sends survives the allowlist unchanged.
    [Fact]
    public void Build_SurvivesTheTelemetryAllowlist()
    {
        NetworkState state = NetworkState.Initial(Now) with { Internet = InternetStatus.Online };
        IReadOnlyDictionary<string, object?> properties = ConnectSessionReadyTelemetry.Build(state, new FoundryConnectConfigurationDocument());

        IReadOnlyDictionary<string, object?> sanitized = TelemetryEventPropertyPolicy.Sanitize(TelemetryEvents.ConnectSessionReady, properties);

        Assert.Equal(properties.OrderBy(static item => item.Key), sanitized.OrderBy(static item => item.Key));
    }
}
