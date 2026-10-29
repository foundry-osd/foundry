// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Network;
using Foundry.Connect.Network.Policy;
using Foundry.Connect.Tests.Support;
using Foundry.Connect.ViewModels;

namespace Foundry.Connect.Tests.ViewModels;

public sealed class NetworkTextKeysTests
{
    public static TheoryData<NetworkOperationKind, NetworkFailureCode, bool> EveryFailure
    {
        get
        {
            var data = new TheoryData<NetworkOperationKind, NetworkFailureCode, bool>();
            foreach (NetworkOperationKind operation in Enum.GetValues<NetworkOperationKind>())
            {
                foreach (NetworkFailureCode code in Enum.GetValues<NetworkFailureCode>().Where(static value => value != NetworkFailureCode.None))
                {
                    data.Add(operation, code, false);
                    data.Add(operation, code, true);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(EveryFailure))]
    public void OutcomeKey_EveryFailureMapsToAnExistingKey(NetworkOperationKind operation, NetworkFailureCode code, bool provisionedPanel)
    {
        string? key = NetworkTextKeys.OutcomeKey(OperationOutcome.Failed(operation, code), provisionedPanel);

        Assert.NotNull(key);
        Assert.True(ConnectResources.Exists(key), key);
    }

    [Theory]
    [InlineData(OperationStatus.Succeeded)]
    [InlineData(OperationStatus.NoChange)]
    [InlineData(OperationStatus.Cancelled)]
    public void OutcomeKey_NonFailureShowsNothing(OperationStatus status)
    {
        Assert.Null(NetworkTextKeys.OutcomeKey(new OperationOutcome(NetworkOperationKind.ConnectWifiNetwork, status), provisionedPanel: false));
    }

    [Theory]
    [InlineData(NetworkOperationKind.ConnectWifiNetwork, NetworkFailureCode.ConnectionFailed, false, "Wifi.ActionConnectFailed")]
    [InlineData(NetworkOperationKind.ConnectProvisionedWifi, NetworkFailureCode.ConnectionFailed, true, "Wifi.ActionProvisionedConnectFailed")]
    [InlineData(NetworkOperationKind.DisconnectWifi, NetworkFailureCode.DisconnectFailed, true, "Wifi.ActionProvisionedDisconnectFailed")]
    [InlineData(NetworkOperationKind.DisconnectWifi, NetworkFailureCode.DisconnectTimedOut, false, "Wifi.ActionDisconnectFailed")]
    [InlineData(NetworkOperationKind.ConnectWifiNetwork, NetworkFailureCode.InvalidPassphrase, false, "Wifi.ActionInvalidPassphrase")]
    [InlineData(NetworkOperationKind.ConnectWifiNetwork, NetworkFailureCode.ConnectTimedOut, false, "Wifi.ActionConnectTimedOut")]
    [InlineData(NetworkOperationKind.ConnectWifiNetwork, NetworkFailureCode.UnsupportedSecurity, false, "Wifi.ActionSelectedNotSupported")]
    [InlineData(NetworkOperationKind.ConnectProvisionedWifi, NetworkFailureCode.WifiSecretUnavailable, true, "Wifi.ActionProvisionedSecretUnavailable")]
    [InlineData(NetworkOperationKind.ConnectProvisionedWifi, NetworkFailureCode.WifiSecretLocked, true, "Wifi.ActionProvisionedLocked")]
    [InlineData(NetworkOperationKind.ApplyProvisioning, NetworkFailureCode.WiredSecretUnavailable, false, "Ethernet.Dot1xReasonSecretUnavailable")]
    [InlineData(NetworkOperationKind.ConnectWifiNetwork, NetworkFailureCode.Unexpected, false, "Wifi.ActionTryAgain")]
    public void OutcomeKey_PicksTheMostSpecificText(NetworkOperationKind operation, NetworkFailureCode code, bool provisionedPanel, string expected)
    {
        Assert.Equal(expected, NetworkTextKeys.OutcomeKey(OperationOutcome.Failed(operation, code), provisionedPanel));
    }

    [Fact]
    public void UnlockCancelledKeys_AreSpecific()
    {
        Assert.Equal("Wifi.ProvisionedUnlockCancelled", NetworkTextKeys.ProvisionedUnavailableKey(ProvisionedWifiUnavailableReason.UnlockCancelled));
        Assert.Equal("Ethernet.Dot1xReasonUnlockCancelled", NetworkTextKeys.Dot1xReasonKey(Dot1xFailureReason.UnlockCancelled));
    }

    [Fact]
    public void Dot1xKeys_CoverEveryValue()
    {
        foreach (Dot1xAuthenticationStatus status in Enum.GetValues<Dot1xAuthenticationStatus>())
        {
            string? key = NetworkTextKeys.Dot1xStatusKey(status);
            Assert.Equal(status == Dot1xAuthenticationStatus.NotConfigured, key is null);
            Assert.True(key is null || ConnectResources.Exists(key), status.ToString());
        }

        foreach (Dot1xFailureReason reason in Enum.GetValues<Dot1xFailureReason>())
        {
            string? key = NetworkTextKeys.Dot1xReasonKey(reason);
            Assert.Equal(reason == Dot1xFailureReason.None, key is null);
            Assert.True(key is null || ConnectResources.Exists(key), reason.ToString());
        }
    }

    [Fact]
    public void StateKeys_CoverEveryValue()
    {
        Assert.All(Enum.GetValues<ProvisionedWifiUnavailableReason>(), reason => Assert.True(ConnectResources.Exists(NetworkTextKeys.ProvisionedUnavailableKey(reason)), reason.ToString()));
        Assert.All(Enum.GetValues<WifiSecurityType>(), security => Assert.True(ConnectResources.Exists(NetworkTextKeys.SecurityKey(security)), security.ToString()));
        Assert.All(Enum.GetValues<WifiProfileSecurity>().Cast<WifiProfileSecurity?>().Append(null), security => Assert.True(ConnectResources.Exists(NetworkTextKeys.ProfileSecurityKey(security)), security?.ToString() ?? "null"));
        Assert.All(Enum.GetValues<BlockedReason>(), reason => Assert.True(ConnectResources.Exists(NetworkTextKeys.BlockedDescriptionKey(reason)), reason.ToString()));

        foreach (ConnectPageStatus status in Enum.GetValues<ConnectPageStatus>())
        {
            foreach (BlockedReason reason in Enum.GetValues<BlockedReason>())
            {
                (string title, string description) = NetworkTextKeys.HeroKeys(status, reason, NetworkOperationKind.ApplyProvisioning);
                Assert.True(ConnectResources.Exists(title) && ConnectResources.Exists(description), $"{status} {reason}");
            }
        }
    }

    [Theory]
    [InlineData(WifiAvailability.Available, null)]
    [InlineData(WifiAvailability.Disabled, null)]
    [InlineData(WifiAvailability.RuntimeMissing, "Wifi.UnavailableAtRuntime")]
    [InlineData(WifiAvailability.NoAdapter, "Wifi.NoAdapterDetected")]
    public void WifiNoticeKey_ExplainsOnlyAMissingWlanStack(WifiAvailability availability, string? expected)
    {
        Assert.Equal(expected, NetworkTextKeys.WifiNoticeKey(availability));
    }

    [Fact]
    public void WifiNoticeKey_CoversEveryValue()
    {
        Assert.All(Enum.GetValues<WifiAvailability>(), availability =>
        {
            string? key = NetworkTextKeys.WifiNoticeKey(availability);
            Assert.Equal(availability is WifiAvailability.RuntimeMissing or WifiAvailability.NoAdapter, key is not null);
            Assert.True(key is null || ConnectResources.Exists(key), availability.ToString());
        });
    }

    [Fact]
    public void HeroKeys_ApplyingProvisioningHasItsOwnDescription()
    {
        Assert.Equal("Status.ApplyingProvisioningDescription", NetworkTextKeys.HeroKeys(ConnectPageStatus.Waiting, BlockedReason.None, NetworkOperationKind.ApplyProvisioning).DescriptionKey);
        Assert.Equal("Status.ConnectingDescription", NetworkTextKeys.HeroKeys(ConnectPageStatus.Waiting, BlockedReason.None, NetworkOperationKind.ConnectWifiNetwork).DescriptionKey);
    }
}
