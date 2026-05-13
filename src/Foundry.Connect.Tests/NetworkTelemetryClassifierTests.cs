using Foundry.Connect.Models.Network;
using Foundry.Connect.Services.Network;

namespace Foundry.Connect.Tests;

public sealed class NetworkTelemetryClassifierTests
{
    [Theory]
    [InlineData("Open", "open")]
    [InlineData("OWE", "owe")]
    [InlineData("WPA2-Personal", "personal")]
    [InlineData("WPA3-Personal", "personal")]
    [InlineData("WPA2-Enterprise", "enterprise")]
    [InlineData("WPA3-Enterprise", "enterprise")]
    [InlineData("Unknown (99)", "unknown")]
    public void ClassifyWifiSecurity_ReturnsStableCategory(string authentication, string expected)
    {
        Assert.Equal(expected, NetworkTelemetryClassifier.ClassifyWifiSecurity(authentication));
    }

    [Fact]
    public void ClassifyConnection_WhenEthernetIsConnected_ReturnsEthernet()
    {
        var snapshot = new NetworkStatusSnapshot
        {
            IsEthernetConnected = true,
            ConnectedWifiSsid = "Foundry"
        };

        Assert.Equal("ethernet", NetworkTelemetryClassifier.ClassifyConnection(snapshot));
    }

    [Fact]
    public void ClassifyConnection_WhenOnlyWifiIsConnected_ReturnsWifi()
    {
        var snapshot = new NetworkStatusSnapshot
        {
            ConnectedWifiSsid = "Foundry"
        };

        Assert.Equal("wifi", NetworkTelemetryClassifier.ClassifyConnection(snapshot));
    }
}
