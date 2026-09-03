// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using Foundry.Connect.Models;
using Foundry.Connect.Models.Network;
using Foundry.Connect.Services.Network;

namespace Foundry.Connect.Tests;

public sealed class NetworkTelemetryClassifierTests
{
    [Theory]
    [MemberData(nameof(NetworkFailures))]
    public void ClassifyFailure_ReturnsSafeStableFields(
        Exception exception,
        bool cancellationRequested,
        string expectedReason,
        string? expectedCode)
    {
        NetworkFailureClassification result = NetworkTelemetryClassifier.ClassifyFailure(exception, cancellationRequested);

        Assert.Equal("network", result.Kind);
        Assert.Equal(expectedReason, result.Reason);
        Assert.Equal(expectedCode, result.Code);
        Assert.DoesNotContain("secret", result.Code ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    public static TheoryData<Exception, bool, string, string?> NetworkFailures => new()
    {
        { new TaskCanceledException("https://secret.invalid/path?token=value"), false, "timeout", null },
        { new OperationCanceledException("secret"), true, "cancelled", null },
        { new HttpRequestException("secret", null, HttpStatusCode.BadGateway), false, "http_status", "502" },
        { new HttpRequestException("secret", null, HttpStatusCode.ProxyAuthenticationRequired), false, "proxy", "407" },
        { new HttpRequestException("secret", new SocketException((int)SocketError.HostNotFound)), false, "dns", SocketError.HostNotFound.ToString() },
        { new HttpRequestException("secret", new AuthenticationException("certificate secret")), false, "tls", null },
        { new HttpRequestException("https://secret.invalid"), false, "transport", null }
    };

    [Theory]
    [InlineData("Wi-Fi profile import failed: simulated error", "profile_import_failed", "wifi_profile_import_failed")]
    [InlineData("Windows started the Wi-Fi connection workflow, but 'Foundry' did not reach the connected state within 10 seconds.", "timeout", "wifi_connect_timeout")]
    [InlineData("No wireless adapter is available to connect the provisioned Wi-Fi profile.", "missing_adapter", "no_wireless_adapter")]
    public void TryClassifyHandledFailure_ReturnsStableFieldsForReturnedStatus(string status, string expectedReason, string expectedCode)
    {
        NetworkFailureClassification? result = NetworkTelemetryClassifier.TryClassifyHandledFailure(status);

        Assert.NotNull(result);
        Assert.Equal("network", result.Kind);
        Assert.Equal(expectedReason, result.Reason);
        Assert.Equal(expectedCode, result.Code);
    }

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

    [Theory]
    [InlineData(NetworkLayoutMode.EthernetOnly, "ethernet_only")]
    [InlineData(NetworkLayoutMode.EthernetWifi, "ethernet_wifi")]
    public void ClassifyLayout_ReturnsStableCategory(NetworkLayoutMode layoutMode, string expected)
    {
        Assert.Equal(expected, NetworkTelemetryClassifier.ClassifyLayout(layoutMode));
    }
}
