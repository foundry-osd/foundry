// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;
using Foundry.Connect.Network;

namespace Foundry.Connect.Tests.Network;

public sealed class NetworkFailureTaxonomyTests
{
    [Theory]
    [InlineData(NetworkFailureCode.NoWirelessAdapter, "missing_adapter", "no_wireless_adapter")]
    [InlineData(NetworkFailureCode.ProvisionedProfileUnavailable, "profile_unavailable", "wifi_profile_unavailable")]
    [InlineData(NetworkFailureCode.WifiProfileRejected, "profile_import_failed", "wifi_profile_import_failed")]
    [InlineData(NetworkFailureCode.ConnectRequestFailed, "connect_request_failed", "wifi_connect_request_failed")]
    [InlineData(NetworkFailureCode.ConnectTimedOut, "timeout", "wifi_connect_timeout")]
    [InlineData(NetworkFailureCode.DisconnectFailed, "disconnect_request_failed", "wifi_disconnect_request_failed")]
    [InlineData(NetworkFailureCode.DisconnectTimedOut, "timeout", "wifi_disconnect_timeout")]
    [InlineData(NetworkFailureCode.UnsupportedSecurity, "unsupported", "wifi_runtime_not_supported")]
    [InlineData(NetworkFailureCode.WifiCertificateImportFailed, "certificate_import_failed", "wifi_certificate_import_failed")]
    [InlineData(NetworkFailureCode.WiredProfileTemplateMissing, "profile_unavailable", "wired_profile_template_missing")]
    [InlineData(NetworkFailureCode.WiredProfileImportFailed, "profile_import_failed", "wired_profile_import_failed")]
    [InlineData(NetworkFailureCode.WiredReconnectFailed, "reconnect_request_failed", "wired_reconnect_request_failed")]
    [InlineData(NetworkFailureCode.WiredCertificateImportFailed, "certificate_import_failed", "wired_certificate_import_failed")]
    public void Describe_KeepsExistingRemoteTaxonomy(NetworkFailureCode code, string reason, string expectedCode)
    {
        NetworkFailureDescriptor descriptor = NetworkFailureTaxonomy.Describe(code);

        Assert.Equal("network", descriptor.Kind);
        Assert.Equal(reason, descriptor.Reason);
        Assert.Equal(expectedCode, descriptor.Code);
    }

    [Fact]
    public void Describe_CoversEveryFailureCode()
    {
        foreach (NetworkFailureCode code in Enum.GetValues<NetworkFailureCode>().Where(static value => value != NetworkFailureCode.None))
        {
            NetworkFailureDescriptor descriptor = NetworkFailureTaxonomy.Describe(code);
            Assert.False(string.IsNullOrWhiteSpace(descriptor.Reason), code.ToString());
            Assert.False(string.IsNullOrWhiteSpace(descriptor.Code), code.ToString());
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => NetworkFailureTaxonomy.Describe(NetworkFailureCode.None));
    }

    [Theory]
    [InlineData(NetworkOperationKind.ApplyProvisioning, "network.apply_provisioned_settings")]
    [InlineData(NetworkOperationKind.ConnectProvisionedWifi, "wifi.provisioned_action")]
    [InlineData(NetworkOperationKind.ConnectWifiNetwork, "wifi.selected_action")]
    [InlineData(NetworkOperationKind.DisconnectWifi, "wifi.disconnect_action")]
    public void OperationName_IsStable(NetworkOperationKind operation, string expected)
    {
        Assert.Equal(expected, NetworkFailureTaxonomy.OperationName(operation));
    }

    [Theory]
    [MemberData(nameof(NetworkFailures))]
    public void ClassifyException_ReturnsSafeStableFields(Exception exception, bool cancellationRequested, string expectedReason, string? expectedCode)
    {
        NetworkFailureDescriptor result = NetworkFailureTaxonomy.ClassifyException(exception, cancellationRequested);

        Assert.Equal("network", result.Kind);
        Assert.Equal(expectedReason, result.Reason);
        Assert.Equal(expectedCode, result.Code);
    }

    public static TheoryData<Exception, bool, string, string?> NetworkFailures => new()
    {
        { new TaskCanceledException("https://secret.invalid/path?token=value"), false, "timeout", null },
        { new OperationCanceledException("secret"), true, "cancelled", null },
        { new HttpRequestException("secret", null, HttpStatusCode.BadGateway), false, "http_status", "502" },
        { new HttpRequestException("secret", null, HttpStatusCode.ProxyAuthenticationRequired), false, "proxy", "407" },
        { new HttpRequestException("secret", new SocketException((int)SocketError.HostNotFound)), false, "dns", SocketError.HostNotFound.ToString() },
        { new HttpRequestException("secret", new AuthenticationException("certificate secret")), false, "tls", null },
        { new InvalidOperationException("https://secret.invalid"), false, "transport", null }
    };
}
