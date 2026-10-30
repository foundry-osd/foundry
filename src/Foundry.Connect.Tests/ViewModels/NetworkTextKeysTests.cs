// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Network;
using Foundry.Connect.Network.Policy;
using Foundry.Connect.Tests.Support;
using Foundry.Connect.ViewModels;
using Foundry.Wpf.Localization;

namespace Foundry.Connect.Tests.ViewModels;

public sealed class NetworkTextKeysTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 8, 0, 0, TimeSpan.Zero);
    private readonly ResourceLocalizationService _localization = TestLocalization.Create();

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

        if (code is NetworkFailureCode.NoWirelessAdapter or NetworkFailureCode.WifiRuntimeMissing)
        {
            Assert.Null(key);
            return;
        }

        Assert.NotNull(key);
        Assert.True(ConnectResources.Exists(key), key);
    }

    [Theory]
    [InlineData(NetworkFailureCode.NoWirelessAdapter)]
    [InlineData(NetworkFailureCode.WifiRuntimeMissing)]
    public void OutcomeKey_AdapterOrRuntimeLossGivesNoFeedback(NetworkFailureCode code)
    {
        Assert.Null(NetworkTextKeys.OutcomeKey(OperationOutcome.Failed(NetworkOperationKind.ConnectWifiNetwork, code)));
        Assert.Null(NetworkTextKeys.OutcomeKey(OperationOutcome.Failed(NetworkOperationKind.ConnectProvisionedWifi, code), provisionedPanel: true));
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
    [InlineData(NetworkOperationKind.ConnectProvisionedWifi, NetworkFailureCode.WifiSecretUnavailable, true, "Wifi.ProvisionedUnavailableSecret")]
    [InlineData(NetworkOperationKind.ConnectProvisionedWifi, NetworkFailureCode.WifiSecretLocked, true, "Wifi.ProvisionedLocked")]
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

        foreach (BlockedReason reason in Enum.GetValues<BlockedReason>())
        {
            foreach (bool hasEthernetAdapter in new[] { true, false })
            {
                foreach (WifiAvailability wifi in Enum.GetValues<WifiAvailability>())
                {
                    foreach (bool isWifiSectionShown in new[] { true, false })
                    {
                        string key = NetworkTextKeys.BlockedDescriptionKey(reason, hasEthernetAdapter, wifi, isWifiSectionShown);
                        Assert.True(ConnectResources.Exists(key), $"{reason} {hasEthernetAdapter} {wifi} {isWifiSectionShown}");
                    }
                }
            }
        }

        NetworkState state = NetworkState.Initial(Now);
        foreach (ConnectPageStatus status in Enum.GetValues<ConnectPageStatus>())
        {
            foreach (BlockedReason reason in Enum.GetValues<BlockedReason>())
            {
                foreach (bool isWifiSectionShown in new[] { true, false })
                {
                    (string title, string? description) = NetworkTextKeys.HeroKeys(status, reason, state, isWifiSectionShown);
                    Assert.True(ConnectResources.Exists(title), $"{status} {reason} {isWifiSectionShown}");
                    Assert.True(description is null || ConnectResources.Exists(description), $"{status} {reason} {isWifiSectionShown}");
                }
            }
        }
    }

    [Theory]
    [InlineData(BlockedReason.NoAdapter, false, WifiAvailability.Disabled, false, "Status.BlockedNoEthernetAdapterDescription")]
    [InlineData(BlockedReason.NoAdapter, false, WifiAvailability.RuntimeMissing, false, "Status.BlockedNoEthernetAdapterDescription")]
    [InlineData(BlockedReason.NoAdapter, false, WifiAvailability.NoAdapter, true, "Status.BlockedNoAdapterDescription")]
    [InlineData(BlockedReason.NoAdapter, false, WifiAvailability.RuntimeMissing, true, "Status.BlockedNoAdapterDescription")]
    [InlineData(BlockedReason.NoLink, true, WifiAvailability.Available, true, "Status.BlockedNoLinkDescription")]
    [InlineData(BlockedReason.NoLink, true, WifiAvailability.NoAdapter, true, "Status.BlockedNoCableDescription")]
    [InlineData(BlockedReason.NoLink, true, WifiAvailability.RuntimeMissing, false, "Status.BlockedNoCableDescription")]
    [InlineData(BlockedReason.NoLink, true, WifiAvailability.Disabled, false, "Status.BlockedNoCableDescription")]
    [InlineData(BlockedReason.NoLink, false, WifiAvailability.Available, true, "Status.BlockedJoinWifiDescription")]
    [InlineData(BlockedReason.WiredAuthentication, true, WifiAvailability.Disabled, false, "Status.BlockedDot1xDescription")]
    [InlineData(BlockedReason.NoIpAddress, true, WifiAvailability.Available, true, "Status.BlockedNoIpAddressDescription")]
    [InlineData(BlockedReason.CaptivePortal, true, WifiAvailability.Disabled, false, "Status.BlockedCaptivePortalDescription")]
    [InlineData(BlockedReason.NoInternet, false, WifiAvailability.Available, true, "Status.BlockedNoInternetDescription")]
    public void BlockedDescriptionKey_OffersOnlyWhatThisDeviceCanDo(BlockedReason reason, bool hasEthernetAdapter, WifiAvailability wifi, bool isWifiSectionShown, string expected)
    {
        Assert.Equal(expected, NetworkTextKeys.BlockedDescriptionKey(reason, hasEthernetAdapter, wifi, isWifiSectionShown));
    }

    [Theory]
    [InlineData(WifiAvailability.Disabled)]
    [InlineData(WifiAvailability.RuntimeMissing)]
    public void BlockedDescriptionKey_WithoutWifiSection_NeverMentionsWifi(WifiAvailability wifi)
    {
        foreach (BlockedReason reason in Enum.GetValues<BlockedReason>())
        {
            foreach (bool hasEthernetAdapter in new[] { true, false })
            {
                string key = NetworkTextKeys.BlockedDescriptionKey(reason, hasEthernetAdapter, wifi, isWifiSectionShown: false);
                Assert.DoesNotContain("Wi-Fi", _localization.GetString(key), StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void WiredTexts_NeverMentionWifi()
    {
        IEnumerable<string> keys = Enum.GetValues<Dot1xFailureReason>().Select(NetworkTextKeys.Dot1xReasonKey)
            .Concat(Enum.GetValues<Dot1xAuthenticationStatus>().Select(NetworkTextKeys.Dot1xStatusKey))
            .OfType<string>()
            .Concat(["Status.Dot1xAuthenticatingDescription", "Status.ApplyingProvisioningDescription", "Status.ConnectingDescription",
                "Ethernet.UnavailableOnDevice", "Ethernet.NoActiveLink", "Ethernet.WaitingConfiguration", "Ethernet.ApipaAddress"]);

        Assert.All(keys, key => Assert.DoesNotContain("Wi-Fi", _localization.GetString(key), StringComparison.Ordinal));
    }

    [Fact]
    public void HeroKeys_CheckingAndReadyHaveNoDescription()
    {
        NetworkState state = NetworkState.Initial(Now);

        Assert.Null(NetworkTextKeys.HeroKeys(ConnectPageStatus.Checking, BlockedReason.None, state, isWifiSectionShown: false).DescriptionKey);
        Assert.Null(NetworkTextKeys.HeroKeys(ConnectPageStatus.Ready, BlockedReason.None, state, isWifiSectionShown: true).DescriptionKey);
    }

    [Theory]
    [InlineData(NetworkOperationKind.ApplyProvisioning, "Status.ConnectingTitle", "Status.ApplyingProvisioningDescription")]
    [InlineData(NetworkOperationKind.ConnectWifiNetwork, "Status.ConnectingTitle", "Status.ConnectingDescription")]
    [InlineData(NetworkOperationKind.ConnectProvisionedWifi, "Status.ConnectingTitle", "Status.ConnectingDescription")]
    [InlineData(NetworkOperationKind.DisconnectWifi, "Status.DisconnectingTitle", "Status.ConnectingDescription")]
    [InlineData(null, "Status.ConnectingTitle", "Status.Dot1xAuthenticatingDescription")]
    public void HeroKeys_WaitingNamesWhatRuns(NetworkOperationKind? operation, string expectedTitle, string expectedDescription)
    {
        NetworkState state = NetworkState.Initial(Now) with { ActiveOperation = operation };

        Assert.Equal((expectedTitle, expectedDescription), NetworkTextKeys.HeroKeys(ConnectPageStatus.Waiting, BlockedReason.None, state, isWifiSectionShown: false));
    }
}
