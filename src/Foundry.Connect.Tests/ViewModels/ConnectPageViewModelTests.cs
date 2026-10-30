// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Runtime.CompilerServices;
using Foundry.Connect.Network;
using Foundry.Connect.Network.Policy;
using Foundry.Connect.Security;
using Foundry.Connect.Tests.Support;
using Foundry.Connect.ViewModels;
using Foundry.Core.Models.Configuration;
using Foundry.Wpf.Localization;
using Microsoft.Extensions.Logging;

namespace Foundry.Connect.Tests.ViewModels;

public sealed class ConnectPageViewModelTests : IDisposable
{
    private readonly ManualTimeProvider _clock = new();
    private readonly FakeConnectionCoordinator _coordinator = new();
    private readonly FakeUnlockService _unlock = new();
    private readonly FakeNetworkMonitor _monitor = new();
    private readonly FakeSuccessfulExitService _exit = new();
    private readonly ResourceLocalizationService _localization = TestLocalization.Create();
    private readonly List<ConnectFocusTarget> _focus = [];
    private readonly ConnectPageViewModel _page;

    public ConnectPageViewModelTests()
    {
        var actions = new ConnectNetworkActions(_coordinator, _unlock, new RecordingLogger<ConnectNetworkActions>());
        var dispatcher = new ImmediateUiDispatcher();
        var configuration = new FoundryConnectConfigurationDocument
        {
            Capabilities = new ConnectNetworkCapabilitiesSettings { WifiProvisioned = true },
            Wifi = new ConnectWifiSettings { IsEnabled = true, Ssid = "Foundry Lab", SecurityType = "WPA3-Personal" }
        };
        _page = new ConnectPageViewModel(
            _localization, _coordinator, _monitor, actions,
            new AutoContinueViewModel(_localization, _exit, new FakeApplicationLifetime(), _clock, dispatcher, new RecordingLogger<AutoContinueViewModel>(), isAutoContinueEnabled: true),
            new EthernetSectionViewModel(_localization),
            new ProvisionedWifiViewModel(_localization, actions, configuration),
            new WifiNetworksViewModel(_localization, actions, _monitor),
            new DiagnosticsViewModel(_localization, new RecordingLogger<DiagnosticsViewModel>()),
            dispatcher, _clock, new RecordingLogger<ConnectPageViewModel>());
        _page.FocusRequested += (_, target) => _focus.Add(target);
    }

    public void Dispose() => _page.Dispose();

    private string Text(string key) => _localization.GetString(key);

    private void Publish(ConnectDebugScenario scenario) => _coordinator.Publish(DebugScenarioCatalog.Create(scenario, _clock.GetUtcNow()));

    public sealed record PageExpectation(
        ConnectPageStatus Status, BlockedReason Reason, string TitleKey, string? DescriptionKey, bool RetryActions, bool TwoColumns,
        bool EthernetCard, bool EthernetUnavailableCard, bool Provisioned, bool WifiCard, bool WifiList, bool WifiUnavailable,
        bool Dot1xRow, bool Dot1xReason, bool CanContinue, ConnectFocusTarget Focus);

    private const string Blocked = "Status.BlockedTitle";

    public static TheoryData<ConnectDebugScenario, PageExpectation> Matrix => new()
    {
        { ConnectDebugScenario.Checking, new(ConnectPageStatus.Checking, BlockedReason.None, "Status.CheckingTitle", null, false, false, false, false, false, false, false, false, false, false, false, ConnectFocusTarget.None) },
        { ConnectDebugScenario.NoAdapter, new(ConnectPageStatus.Blocked, BlockedReason.NoAdapter, "Status.BlockedNoAdapterTitle", "Status.BlockedNoEthernetAdapterDescription", true, false, false, true, false, false, false, false, false, false, false, ConnectFocusTarget.Retry) },
        { ConnectDebugScenario.NoLink, new(ConnectPageStatus.Blocked, BlockedReason.NoLink, "Status.BlockedNoCableTitle", "Status.BlockedNoCableDescription", true, false, true, false, false, false, false, false, false, false, false, ConnectFocusTarget.Retry) },
        { ConnectDebugScenario.WaitingForAddress, new(ConnectPageStatus.Blocked, BlockedReason.NoIpAddress, "Status.BlockedNoIpAddressTitle", "Status.BlockedNoIpAddressDescription", true, false, true, false, false, false, false, false, false, false, false, ConnectFocusTarget.Retry) },
        { ConnectDebugScenario.ApipaAddress, new(ConnectPageStatus.Blocked, BlockedReason.NoIpAddress, "Status.BlockedNoIpAddressTitle", "Status.BlockedNoIpAddressDescription", true, false, true, false, false, false, false, false, false, false, false, ConnectFocusTarget.Retry) },
        { ConnectDebugScenario.EthernetNoInternet, new(ConnectPageStatus.Blocked, BlockedReason.NoInternet, Blocked, "Status.BlockedNoInternetDescription", true, false, true, false, false, false, false, false, false, false, false, ConnectFocusTarget.Retry) },
        { ConnectDebugScenario.CaptivePortal, new(ConnectPageStatus.Blocked, BlockedReason.CaptivePortal, "Status.BlockedCaptivePortalTitle", "Status.BlockedCaptivePortalSignInDescription", true, false, true, false, false, false, false, false, false, false, false, ConnectFocusTarget.Retry) },
        { ConnectDebugScenario.EthernetReady, new(ConnectPageStatus.Ready, BlockedReason.None, "Status.NetworkReadyTitle", null, false, false, true, false, false, false, false, false, false, false, true, ConnectFocusTarget.Continue) },
        { ConnectDebugScenario.Dot1xFailed, new(ConnectPageStatus.Blocked, BlockedReason.WiredAuthentication, "Status.BlockedDot1xTitle", "Status.BlockedDot1xDescription", true, false, true, false, false, false, false, false, true, true, false, ConnectFocusTarget.Retry) },
        { ConnectDebugScenario.WifiRuntimeMissing, new(ConnectPageStatus.Blocked, BlockedReason.NoLink, "Status.BlockedNoCableTitle", "Status.BlockedNoCableDescription", true, false, true, false, false, false, false, false, false, false, false, ConnectFocusTarget.Retry) },
        { ConnectDebugScenario.NoWifiAdapter, new(ConnectPageStatus.Blocked, BlockedReason.NoLink, "Status.BlockedNoCableTitle", "Status.BlockedNoCableDescription", true, true, true, false, false, true, false, true, false, false, false, ConnectFocusTarget.Retry) },
        { ConnectDebugScenario.WifiNetworks, new(ConnectPageStatus.Blocked, BlockedReason.NoLink, "Status.BlockedNoLinkTitle", "Status.BlockedNoLinkDescription", true, true, true, false, false, true, true, false, false, false, false, ConnectFocusTarget.WifiNetworks) },
        { ConnectDebugScenario.ProvisionedWifiUnavailable, new(ConnectPageStatus.Blocked, BlockedReason.NoLink, "Status.BlockedNoLinkTitle", "Status.BlockedNoLinkDescription", true, true, true, false, true, true, true, false, false, false, false, ConnectFocusTarget.WifiNetworks) },
        { ConnectDebugScenario.WifiReady, new(ConnectPageStatus.Ready, BlockedReason.None, "Status.NetworkReadyTitle", null, false, true, true, false, true, true, true, false, false, false, true, ConnectFocusTarget.Continue) },
        { ConnectDebugScenario.ProvisionedWifiLocked, new(ConnectPageStatus.Blocked, BlockedReason.NoLink, "Status.BlockedNoLinkTitle", "Status.BlockedNoLinkDescription", true, true, true, false, true, true, true, false, false, false, false, ConnectFocusTarget.ProvisionedConnect) },
        { ConnectDebugScenario.Dot1xAuthenticating, new(ConnectPageStatus.Waiting, BlockedReason.None, "Status.ConnectingTitle", "Status.Dot1xAuthenticatingDescription", true, false, true, false, false, false, false, false, true, false, false, ConnectFocusTarget.None) },
        { ConnectDebugScenario.WifiConnecting, new(ConnectPageStatus.Waiting, BlockedReason.None, "Status.ConnectingTitle", "Status.ConnectingDescription", false, true, true, false, true, true, true, false, false, false, false, ConnectFocusTarget.None) },
        { ConnectDebugScenario.WifiOnly, new(ConnectPageStatus.Blocked, BlockedReason.NoLink, "Status.BlockedNoLinkTitle", "Status.BlockedJoinWifiDescription", true, true, false, true, true, true, true, false, false, false, false, ConnectFocusTarget.ProvisionedConnect) }
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public void Scenario_RendersTheExpectedPage(ConnectDebugScenario scenario, PageExpectation expected)
    {
        Publish(scenario);

        Assert.Equal(expected.Status, _page.Status);
        Assert.Equal(expected.Reason, _page.BlockedReason);
        Assert.Equal(Text(expected.TitleKey), _page.BannerTitle);
        Assert.Equal(expected.DescriptionKey is null ? string.Empty : Text(expected.DescriptionKey), _page.BannerDescription);
        Assert.Equal(expected.DescriptionKey is not null, _page.HasBannerDescription);
        Assert.Equal(expected.Status is ConnectPageStatus.Checking or ConnectPageStatus.Waiting, _page.IsBannerBusy);
        Assert.Equal(expected.RetryActions, _page.ShowRetryActions);
        Assert.Equal(expected.TwoColumns, _page.UsesTwoColumns);
        Assert.Equal(expected.EthernetCard, _page.Ethernet.ShowCard);
        Assert.Equal(expected.EthernetUnavailableCard, _page.Ethernet.ShowUnavailableCard);
        Assert.Equal(expected.Provisioned, _page.ProvisionedWifi.IsVisible);
        Assert.Equal(expected.WifiCard, _page.WifiNetworks.IsVisible);
        Assert.Equal(expected.WifiList, _page.WifiNetworks.IsListVisible);
        Assert.Equal(expected.WifiUnavailable, _page.WifiNetworks.HasUnavailableMessage);
        Assert.Equal(expected.Dot1xRow, _page.Ethernet.HasDot1x);
        Assert.Equal(expected.Dot1xReason, _page.Ethernet.HasDot1xReason);
        Assert.Equal(expected.CanContinue, _page.AutoContinue.ContinueCommand.CanExecute(null));
        Assert.Equal(expected.Focus == ConnectFocusTarget.None ? [] : [expected.Focus], _focus);
    }

    public static TheoryData<ConnectDebugScenario> SimulatedScenarios =>
        new(DebugScenarioCatalog.All.Where(static scenario => scenario != ConnectDebugScenario.Live));

    // The banner never sends the technician to a section the page does not show, and never offers a refresh that
    // cannot list anything.
    [Theory]
    [MemberData(nameof(SimulatedScenarios))]
    public void Scenario_BannerNamesOnlyShownSectionsAndOffersNoImpossibleRefresh(ConnectDebugScenario scenario)
    {
        Publish(scenario);

        bool isEthernetSectionShown = _page.Ethernet.ShowCard || _page.Ethernet.ShowUnavailableCard;
        string[] namesEthernetSection = ["Status.BlockedNoAdapterDescription", "Status.BlockedNoEthernetAdapterDescription", "Status.BlockedDot1xDescription"];
        if (namesEthernetSection.Any(key => Text(key) == _page.BannerDescription))
        {
            Assert.True(isEthernetSectionShown);
        }

        if (Text("Status.BlockedNoAdapterDescription") == _page.BannerDescription)
        {
            Assert.True(_page.WifiNetworks.IsVisible);
        }

        Assert.False(_page.WifiNetworks.HasUnavailableMessage && _page.WifiNetworks.IsListVisible);
    }

    [Theory]
    [InlineData(ConnectDebugScenario.Checking, null, null, false)]
    [InlineData(ConnectDebugScenario.NoAdapter, null, null, true)]
    [InlineData(ConnectDebugScenario.NoLink, "Ethernet.NoActiveLink", null, false)]
    [InlineData(ConnectDebugScenario.WaitingForAddress, "Ethernet.WaitingConfiguration", null, false)]
    [InlineData(ConnectDebugScenario.ApipaAddress, "Ethernet.WaitingConfiguration", null, false)]
    [InlineData(ConnectDebugScenario.EthernetNoInternet, "Common.Connected", null, false)]
    [InlineData(ConnectDebugScenario.CaptivePortal, "Common.Connected", null, false)]
    [InlineData(ConnectDebugScenario.EthernetReady, "Common.Connected", null, false)]
    [InlineData(ConnectDebugScenario.Dot1xFailed, "Ethernet.WaitingConfiguration", null, false)]
    [InlineData(ConnectDebugScenario.WifiRuntimeMissing, "Ethernet.NoActiveLink", null, false)]
    [InlineData(ConnectDebugScenario.NoWifiAdapter, "Ethernet.NoActiveLink", null, false)]
    [InlineData(ConnectDebugScenario.WifiNetworks, "Ethernet.NoActiveLink", null, false)]
    [InlineData(ConnectDebugScenario.ProvisionedWifiUnavailable, "Ethernet.NoActiveLink", "Common.Unavailable", false)]
    [InlineData(ConnectDebugScenario.WifiReady, "Ethernet.NoActiveLink", "Common.Connected", false)]
    [InlineData(ConnectDebugScenario.ProvisionedWifiLocked, "Ethernet.NoActiveLink", "Wifi.StatusLocked", false)]
    [InlineData(ConnectDebugScenario.Dot1xAuthenticating, "Ethernet.WaitingConfiguration", null, false)]
    [InlineData(ConnectDebugScenario.WifiConnecting, "Ethernet.NoActiveLink", "Status.ConnectingTitle", false)]
    [InlineData(ConnectDebugScenario.WifiOnly, null, "Wifi.StatusNotInRange", true)]
    public void Scenario_ShowsTheSectionTexts(ConnectDebugScenario scenario, string? ethernetPillKey, string? provisionedPillKey, bool ethernetUnavailableCard)
    {
        Publish(scenario);

        Assert.Equal(ethernetPillKey is not null, _page.Ethernet.ShowCard);
        if (ethernetPillKey is not null)
        {
            Assert.Equal(Text(ethernetPillKey), _page.Ethernet.StatusText);
        }

        Assert.Equal(provisionedPillKey is not null, _page.ProvisionedWifi.IsVisible);
        if (provisionedPillKey is not null)
        {
            Assert.Equal(Text(provisionedPillKey), _page.ProvisionedWifi.StatusText);
        }

        Assert.Equal(ethernetUnavailableCard, _page.Ethernet.ShowUnavailableCard);
        Assert.Equal(Text("Ethernet.UnavailableOnDevice"), _page.Ethernet.NoAdapterText);
    }

    [Fact]
    public void Layout_IsChosenAtTheFirstObservationOnly()
    {
        Publish(ConnectDebugScenario.WifiNetworks);
        Assert.True(_page.UsesTwoColumns);

        _coordinator.Publish(DebugScenarioCatalog.Create(ConnectDebugScenario.WifiRuntimeMissing, _clock.GetUtcNow()));
        Assert.True(_page.UsesTwoColumns);
        Assert.True(_page.WifiNetworks.IsVisible);
        Assert.True(_page.WifiNetworks.HasUnavailableMessage);
        Assert.False(_page.ProvisionedWifi.IsVisible);

        Publish(ConnectDebugScenario.WifiNetworks);
        Assert.True(_page.WifiNetworks.ShowNetworkList);
    }

    [Fact]
    public void OneColumnSession_ShowsWifiOnceItWorksWithoutSwitchingTheLayout()
    {
        Publish(ConnectDebugScenario.WifiRuntimeMissing);
        Assert.False(_page.UsesTwoColumns);
        Assert.False(_page.WifiNetworks.IsVisible);

        Publish(ConnectDebugScenario.WifiNetworks);

        Assert.False(_page.UsesTwoColumns);
        Assert.True(_page.WifiNetworks.ShowNetworkList);
    }

    [Fact]
    public void EthernetReadFailure_ShowsTheUnavailableCardWithoutChangingTheLayout()
    {
        Publish(ConnectDebugScenario.WifiNetworks);

        _coordinator.Publish(DebugScenarioCatalog.Create(ConnectDebugScenario.WifiNetworks, _clock.GetUtcNow()) with { Ethernet = EthernetState.Empty });

        Assert.True(_page.UsesTwoColumns);
        Assert.True(_page.Ethernet.ShowUnavailableCard);
        Assert.Equal(Text("Ethernet.UnavailableOnDevice"), _page.Ethernet.NoAdapterText);
        Assert.Equal(Text("Status.BlockedJoinWifiDescription"), _page.BannerDescription);
    }

    [Fact]
    public void NoAdapterAtAll_OnWifiMedia_PointsToBothSections()
    {
        _coordinator.Publish(DebugScenarioCatalog.Create(ConnectDebugScenario.NoWifiAdapter, _clock.GetUtcNow()) with { Ethernet = EthernetState.Empty });

        Assert.Equal(BlockedReason.NoAdapter, _page.BlockedReason);
        Assert.Equal(Text("Status.BlockedNoAdapterDescription"), _page.BannerDescription);
        Assert.True(_page.Ethernet.ShowUnavailableCard);
        Assert.True(_page.WifiNetworks.HasUnavailableMessage);
    }

    public static TheoryData<ConnectDebugScenario> ScenariosWithoutWifi => new(
        ConnectDebugScenario.Checking, ConnectDebugScenario.NoAdapter, ConnectDebugScenario.NoLink, ConnectDebugScenario.WaitingForAddress,
        ConnectDebugScenario.ApipaAddress, ConnectDebugScenario.EthernetNoInternet, ConnectDebugScenario.CaptivePortal, ConnectDebugScenario.EthernetReady,
        ConnectDebugScenario.Dot1xFailed, ConnectDebugScenario.WifiRuntimeMissing, ConnectDebugScenario.Dot1xAuthenticating);

    [Theory]
    [MemberData(nameof(ScenariosWithoutWifi))]
    public void WithoutWifi_NoVisibleTextMentionsWifi(ConnectDebugScenario scenario)
    {
        Publish(scenario);

        List<string> visible = [_page.BannerTitle, _page.BannerDescription, _page.ConnectionChipText, _page.AutoContinue.CaptionText];
        if (_page.Ethernet.ShowCard)
        {
            visible.AddRange([_page.Ethernet.StatusText, _page.Ethernet.SecondaryStatusText, _page.Ethernet.ConfigurationText, _page.Ethernet.Dot1xStatusText, _page.Ethernet.Dot1xReasonText]);
        }

        if (_page.Ethernet.ShowUnavailableCard)
        {
            visible.Add(_page.Ethernet.NoAdapterText);
        }

        Assert.False(_page.UsesTwoColumns);
        Assert.False(_page.ProvisionedWifi.IsVisible);
        Assert.False(_page.WifiNetworks.IsVisible);
        Assert.All(visible, text => Assert.DoesNotContain("Wi-Fi", text, StringComparison.Ordinal));
    }

    public static TheoryData<ProvisionedWifiState> WifiOnlyProfiles => new(
        new ProvisionedWifiState(ProvisionedWifiStatus.Ready, Ssid: WifiSsid.FromText("Foundry Lab")),
        new ProvisionedWifiState(ProvisionedWifiStatus.Unavailable, ProvisionedWifiUnavailableReason.SecretUnavailable, WifiSsid.FromText("Foundry Lab")),
        new ProvisionedWifiState(ProvisionedWifiStatus.Locked, ProvisionedWifiUnavailableReason.UnlockCancelled, WifiSsid.FromText("Foundry Lab")));

    [Theory]
    [MemberData(nameof(WifiOnlyProfiles))]
    public void WithoutEthernetAdapter_NoTextOffersEthernet(ProvisionedWifiState profile)
    {
        NetworkState state = DebugScenarioCatalog.Create(ConnectDebugScenario.WifiOnly, _clock.GetUtcNow());
        _coordinator.Publish(state with { Wifi = state.Wifi with { Provisioned = profile } });

        // The rendered texts; every culture keeps the word "Ethernet". The Ethernet card text names its own section and is left out.
        List<string> visible = [_page.BannerTitle, _page.BannerDescription, _page.ConnectionChipText, _page.AutoContinue.CaptionText];
        if (_page.ProvisionedWifi.IsVisible)
        {
            visible.AddRange([_page.ProvisionedWifi.StatusText, _page.ProvisionedWifi.MessageText]);
        }

        if (_page.WifiNetworks.IsVisible)
        {
            visible.AddRange([_page.WifiNetworks.UnavailableMessageText, _page.WifiNetworks.DetailMessageText]);
            if (_page.WifiNetworks.ShowEmptyText)
            {
                visible.Add(_page.WifiNetworks.EmptyText);
            }
        }

        Assert.True(_page.Ethernet.ShowUnavailableCard);
        Assert.All(visible, text => Assert.DoesNotContain("Ethernet", text, StringComparison.Ordinal));
    }

    [Fact]
    public void ApplyingProvisioningAtStartup_HidesTheEthernetUnavailableCard()
    {
        _coordinator.Publish(NetworkState.Initial(_clock.GetUtcNow()) with { ActiveOperation = NetworkOperationKind.ApplyProvisioning });

        Assert.Equal(ConnectPageStatus.Waiting, _page.Status);
        Assert.Equal(Text("Status.ApplyingProvisioningDescription"), _page.BannerDescription);
        Assert.False(_page.Ethernet.ShowUnavailableCard);
        Assert.False(_page.ShowRetryActions);
        Assert.False(_page.UsesTwoColumns);
    }

    [Theory]
    [MemberData(nameof(SimulatedScenarios))]
    public void Scenario_NeverShowsTheSameMessageTwice(ConnectDebugScenario scenario)
    {
        Publish(scenario);

        List<string> messages = [_page.BannerDescription];
        if (_page.Ethernet.HasDot1xReason)
        {
            messages.Add(_page.Ethernet.Dot1xReasonText);
        }

        if (_page.Ethernet.ShowUnavailableCard)
        {
            messages.Add(_page.Ethernet.NoAdapterText);
        }

        if (_page.ProvisionedWifi.IsVisible && _page.ProvisionedWifi.HasMessage)
        {
            messages.Add(_page.ProvisionedWifi.MessageText);
        }

        if (_page.WifiNetworks.HasUnavailableMessage)
        {
            messages.Add(_page.WifiNetworks.UnavailableMessageText);
        }

        if (_page.WifiNetworks.ShowEmptyText)
        {
            messages.Add(_page.WifiNetworks.EmptyText);
        }

        List<string> shown = messages.Where(static text => text.Length > 0).ToList();
        Assert.Equal(shown.Count, shown.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void WifiReady_ShowsTheProvisionedChipAndCountsDown()
    {
        Publish(ConnectDebugScenario.WifiReady);

        Assert.Equal(_localization.Format("Wifi.ConnectionChipProvisionedFormat", "Foundry Lab"), _page.ConnectionChipText);
        Assert.True(_page.AutoContinue.IsCounting);
    }

    [Fact]
    public void FocusRequested_OnlyOnStatusTransitions()
    {
        Publish(ConnectDebugScenario.EthernetNoInternet);
        _coordinator.Publish(DebugScenarioCatalog.Create(ConnectDebugScenario.EthernetNoInternet, _clock.GetUtcNow().AddSeconds(10)));
        Publish(ConnectDebugScenario.EthernetReady);

        Assert.Equal([ConnectFocusTarget.Retry, ConnectFocusTarget.Continue], _focus);
    }

    [Fact]
    public void Blocked_WithAConnectableProvisionedProfile_FocusesItsConnectButton()
    {
        NetworkState state = DebugScenarioCatalog.Create(ConnectDebugScenario.WifiNetworks, _clock.GetUtcNow());

        _coordinator.Publish(state with
        {
            Wifi = state.Wifi with { Provisioned = new ProvisionedWifiState(ProvisionedWifiStatus.Ready, Ssid: WifiSsid.FromText("Foundry Lab")) }
        });

        Assert.Equal(ConnectPageStatus.Blocked, _page.Status);
        Assert.Equal([ConnectFocusTarget.ProvisionedConnect], _focus);
    }

    [Fact]
    public void OnlineLost_DuringTheCountdown_StopsItAndFocusesRetry()
    {
        Publish(ConnectDebugScenario.EthernetReady);
        Assert.True(_page.AutoContinue.IsCounting);

        Publish(ConnectDebugScenario.EthernetNoInternet);

        Assert.False(_page.AutoContinue.IsCounting);
        Assert.False(_page.AutoContinue.ContinueCommand.CanExecute(null));
        Assert.Equal([ConnectFocusTarget.Continue, ConnectFocusTarget.Retry], _focus);
    }

    [Fact]
    public void NetworkingInteraction_PausesTheCountdownAndKeepsFocus()
    {
        Publish(ConnectDebugScenario.WifiReady);
        _focus.Clear();

        _page.WifiNetworks.SelectedNetwork = _page.WifiNetworks.Networks[1];
        Publish(ConnectDebugScenario.WifiNetworks);

        Assert.Equal(AutoContinueStatus.Paused, _page.AutoContinue.State.Status);
        Assert.Empty(_focus);
    }

    [Fact]
    public void Ready_AfterANetworkingInteraction_KeepsFocusAndOffersContinueWithoutCounting()
    {
        Publish(ConnectDebugScenario.WifiNetworks);
        _focus.Clear();
        _page.WifiNetworks.SelectedNetwork = _page.WifiNetworks.Networks[0];

        Publish(ConnectDebugScenario.WifiReady);

        Assert.Equal(ConnectPageStatus.Ready, _page.Status);
        Assert.Empty(_focus);
        Assert.Equal(AutoContinueStatus.Paused, _page.AutoContinue.State.Status);
        Assert.False(_page.AutoContinue.IsContinueDefault);
        Assert.True(_page.AutoContinue.ContinueCommand.CanExecute(null));
    }

    [Fact]
    public void StayHere_MovesTheFocusToContinue()
    {
        Publish(ConnectDebugScenario.EthernetReady);
        _focus.Clear();

        _page.AutoContinue.StayHereCommand.Execute(null);

        Assert.Equal([ConnectFocusTarget.Continue], _focus);
    }

    [Fact]
    public async Task Simulation_IgnoresLiveStateAndNeverActs()
    {
        _page.ApplyDebugScenario(ConnectDebugScenario.WifiNetworks);
        _coordinator.Publish(DebugScenarioCatalog.Create(ConnectDebugScenario.EthernetReady, _clock.GetUtcNow()));
        Assert.True(_page.IsSimulation);
        Assert.Equal(BlockedReason.NoLink, _page.BlockedReason);

        _page.WifiNetworks.SelectedNetwork = _page.WifiNetworks.Networks.Single(item => item.DisplayName == "Workshop");
        _page.WifiNetworks.SetPassphrase("password1".AsSpan());
        await _page.WifiNetworks.ConnectCommand.ExecuteAsync(null);
        _page.ApplyDebugScenario(ConnectDebugScenario.EthernetReady);
        for (int second = 0; second < 10; second++)
        {
            _clock.Advance(AutoContinueViewModel.TickInterval);
        }

        await (_page.AutoContinue.PendingCompletion ?? Task.CompletedTask);
        Assert.Empty(_coordinator.ConnectNetworkCalls);
        Assert.Equal(0, _exit.Calls);

        _page.ApplyDebugScenario(ConnectDebugScenario.Live);
        Assert.False(_page.IsSimulation);
        Assert.Equal(ConnectPageStatus.Ready, _page.Status);
    }

    [Fact]
    public void LanguageChange_RerendersWithoutRefreshOrFocus()
    {
        CultureInfo original = CultureInfo.CurrentUICulture;
        Publish(ConnectDebugScenario.WifiNetworks);
        _focus.Clear();
        WifiNetworkItemViewModel row = _page.WifiNetworks.Networks[0];
        List<string?> raised = [];
        _page.PropertyChanged += (_, args) => raised.Add($"Page.{args.PropertyName}");
        row.PropertyChanged += (_, args) => raised.Add($"Row.{args.PropertyName}");
        _page.AutoContinue.PropertyChanged += (_, args) => raised.Add($"AutoContinue.{args.PropertyName}");

        try
        {
            _localization.SetCulture(CultureInfo.GetCultureInfo(original.Name == "fr-FR" ? "de-DE" : "fr-FR"));

            Assert.Equal(Text("Status.BlockedNoLinkTitle"), _page.BannerTitle);
            Assert.Equal(Text("Status.BlockedNoLinkDescription"), _page.BannerDescription);
            Assert.Equal(0, _monitor.RefreshRequests);
            Assert.Empty(_focus);
            Assert.Same(row, _page.WifiNetworks.Networks[0]);
            Assert.Contains("Page.BannerTitle", raised);
            Assert.Contains("Page.BannerDescription", raised);
            Assert.Contains("Page.ConnectionChipText", raised);
            Assert.Contains("Row.SecurityText", raised);
            Assert.Contains("Row.AutomationName", raised);
            Assert.Contains("AutoContinue.CaptionText", raised);
        }
        finally
        {
            _localization.SetCulture(original);
        }
    }

    [Theory]
    [InlineData(ConnectUnlockResult.Unlocked)]
    [InlineData(ConnectUnlockResult.Cancelled)]
    public async Task InitializeAsync_UnlocksWiredBeforeProvisioning(ConnectUnlockResult unlockResult)
    {
        _unlock.IsWiredUnlockRequired = true;
        _unlock.Result = unlockResult;
        int provisioningCallsAtUnlock = -1;
        _unlock.OnUnlock = () => provisioningCallsAtUnlock = _coordinator.ApplyProvisioningCalls;

        await _page.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal([ConnectUnlockTrigger.WiredStartup], _unlock.Calls);
        Assert.Equal(0, provisioningCallsAtUnlock);
        Assert.Equal(1, _coordinator.ApplyProvisioningCalls);
        Assert.Equal(1, _monitor.RunCalls);
    }

    [Fact]
    public async Task InitializeAsync_AfterTheWiredPrompt_StartsTheCountdownAndFocusesContinueOnceOnline()
    {
        _unlock.IsWiredUnlockRequired = true;
        _unlock.Result = ConnectUnlockResult.Unlocked;
        await _page.InitializeAsync(TestContext.Current.CancellationToken);

        Publish(ConnectDebugScenario.EthernetReady);

        Assert.True(_page.AutoContinue.IsCounting);
        Assert.True(_page.AutoContinue.IsContinueDefault);
        Assert.Equal([ConnectFocusTarget.Continue], _focus);
    }

    [Fact]
    public async Task InitializeAsync_OnlineWhileTheWiredPromptIsOpen_StartsTheCountdownOnlyOnceItCloses()
    {
        _unlock.IsWiredUnlockRequired = true;
        _unlock.Result = ConnectUnlockResult.Unlocked;
        bool countingWhilePrompting = true;
        _unlock.OnUnlock = () =>
        {
            Publish(ConnectDebugScenario.EthernetReady);
            _clock.Advance(TimeSpan.FromSeconds(11));
            countingWhilePrompting = _page.AutoContinue.IsCounting;
        };

        await _page.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.False(countingWhilePrompting);
        Assert.Equal(0, _exit.Calls);
        Assert.True(_page.AutoContinue.IsCounting);
        Assert.Equal(AutoContinuePolicy.CountdownSeconds, _page.AutoContinue.SecondsRemaining);
    }

    [Fact]
    public void UnlockRequested_PromptsForWifiUnlessSimulatedOrDisposed()
    {
        _unlock.IsWifiUnlockRequired = true;

        _coordinator.RaiseUnlockRequested();
        _page.ApplyDebugScenario(ConnectDebugScenario.WifiNetworks);
        _coordinator.RaiseUnlockRequested();
        _page.ApplyDebugScenario(ConnectDebugScenario.Live);
        _page.Dispose();
        _coordinator.RaiseUnlockRequested();

        Assert.Equal([ConnectUnlockTrigger.AutomaticWifi], _unlock.Calls);
    }

    [Fact]
    public void AutomaticUnlock_ThenOnline_StartsTheCountdownAndFocusesContinue()
    {
        _unlock.IsWifiUnlockRequired = true;
        _unlock.Result = ConnectUnlockResult.Unlocked;
        Publish(ConnectDebugScenario.EthernetNoInternet);
        _focus.Clear();

        _coordinator.RaiseUnlockRequested();
        Publish(ConnectDebugScenario.WifiReady);

        Assert.Equal(1, _coordinator.RefreshProvisionedWifiCalls);
        Assert.Equal(ConnectPageStatus.Ready, _page.Status);
        Assert.True(_page.AutoContinue.IsCounting);
        Assert.True(_page.AutoContinue.IsContinueDefault);
        Assert.Equal([ConnectFocusTarget.Continue], _focus);
    }

    [Fact]
    public void EthernetOnline_WhileTheAutomaticPromptIsOpen_StartsTheCountdownOnlyOnceItCloses()
    {
        _unlock.IsWifiUnlockRequired = true;
        _unlock.Result = ConnectUnlockResult.Cancelled;
        Publish(ConnectDebugScenario.EthernetNoInternet);
        bool countingWhilePrompting = true;
        _unlock.OnUnlock = () =>
        {
            Publish(ConnectDebugScenario.EthernetReady);
            for (int second = 0; second <= 10; second++)
            {
                _clock.Advance(AutoContinueViewModel.TickInterval);
            }

            countingWhilePrompting = _page.AutoContinue.IsCounting;
        };

        _coordinator.RaiseUnlockRequested();

        Assert.False(countingWhilePrompting);
        Assert.Null(_page.AutoContinue.PendingCompletion);
        Assert.Equal(0, _exit.Calls);
        Assert.Equal(ConnectPageStatus.Ready, _page.Status);
        Assert.True(_page.AutoContinue.IsCounting);
        Assert.Equal(AutoContinuePolicy.CountdownSeconds, _page.AutoContinue.SecondsRemaining);
    }

    [Fact]
    public async Task ProvisionedConnectPrompt_StillCountsAsAnInteraction()
    {
        _unlock.IsWifiUnlockRequired = true;
        _unlock.Result = ConnectUnlockResult.Unlocked;
        Publish(ConnectDebugScenario.ProvisionedWifiLocked);
        _focus.Clear();

        await _page.ProvisionedWifi.ConnectCommand.ExecuteAsync(null);
        Publish(ConnectDebugScenario.WifiReady);

        Assert.Equal([ConnectUnlockTrigger.ManualWifi], _unlock.Calls);
        Assert.Equal(AutoContinueStatus.Paused, _page.AutoContinue.State.Status);
        Assert.False(_page.AutoContinue.IsContinueDefault);
        Assert.Empty(_focus);
    }

    private NetworkState ConnectedWithoutInternet(NetworkOperationKind operation = NetworkOperationKind.ConnectProvisionedWifi) =>
        DebugScenarioCatalog.Create(ConnectDebugScenario.WifiReady, _clock.GetUtcNow()) with
        {
            Internet = InternetStatus.NoInternet,
            LastOutcome = OperationOutcome.Success(operation)
        };

    [Fact]
    public void Connected_WithoutInternetYet_ShowsTheCheckRecheckingEveryTwoSecondsThenBlocksAfterTen()
    {
        Publish(ConnectDebugScenario.WifiConnecting);

        _coordinator.Publish(ConnectedWithoutInternet());

        Assert.Equal(ConnectPageStatus.Checking, _page.Status);
        Assert.Equal(Text("Status.CheckingTitle"), _page.BannerTitle);
        Assert.False(_page.HasBannerDescription);
        Assert.False(_page.ShowRetryActions);
        for (int recheck = 1; recheck <= 4; recheck++)
        {
            _clock.Advance(TimeSpan.FromSeconds(2));
            _coordinator.Publish(_coordinator.State with { ObservedAt = _clock.GetUtcNow() });
            Assert.Equal(recheck, _monitor.RefreshRequests);
            Assert.Equal(ConnectPageStatus.Checking, _page.Status);
        }

        _clock.Advance(TimeSpan.FromSeconds(2));

        Assert.Equal(ConnectPageStatus.Blocked, _page.Status);
        Assert.Equal(BlockedReason.NoInternet, _page.BlockedReason);
        Assert.Equal(Text("Status.BlockedTitle"), _page.BannerTitle);
        Assert.True(_page.ShowRetryActions);
        Assert.Equal([ConnectFocusTarget.WifiNetworks], _focus);
        _clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(4, _monitor.RefreshRequests);
        Assert.Equal(0, _clock.ActiveTimerCount);
    }

    [Fact]
    public void Connected_OnlineWithinTheRecheck_IsReadyAndCountsDown()
    {
        Publish(ConnectDebugScenario.WifiConnecting);
        _coordinator.Publish(ConnectedWithoutInternet(NetworkOperationKind.ConnectWifiNetwork));
        _clock.Advance(TimeSpan.FromSeconds(2));

        _coordinator.Publish(_coordinator.State with { Internet = InternetStatus.Online });

        Assert.Equal(ConnectPageStatus.Ready, _page.Status);
        Assert.True(_page.AutoContinue.IsCounting);
        Assert.Equal([ConnectFocusTarget.Continue], _focus);
        _clock.Advance(TimeSpan.FromSeconds(4));
        Assert.Equal(1, _monitor.RefreshRequests);
    }

    [Fact]
    public void EthernetAddressObtained_WithoutInternetYet_ShowsTheCheckUntilTheRecheckEnds()
    {
        Publish(ConnectDebugScenario.WaitingForAddress);
        _focus.Clear();

        Publish(ConnectDebugScenario.EthernetNoInternet);
        Assert.Equal(ConnectPageStatus.Checking, _page.Status);
        Assert.Empty(_focus);

        _clock.Advance(TimeSpan.FromSeconds(10));

        Assert.Equal(ConnectPageStatus.Blocked, _page.Status);
        Assert.Equal(BlockedReason.NoInternet, _page.BlockedReason);
        Assert.Equal([ConnectFocusTarget.Retry], _focus);
    }

    [Fact]
    public void ApplyDebugScenario_EndsTheRecheck()
    {
        Publish(ConnectDebugScenario.WaitingForAddress);
        Publish(ConnectDebugScenario.EthernetNoInternet);

        _page.ApplyDebugScenario(ConnectDebugScenario.EthernetNoInternet);
        _clock.Advance(TimeSpan.FromSeconds(10));

        Assert.Equal(ConnectPageStatus.Blocked, _page.Status);
        Assert.Equal(0, _monitor.RefreshRequests);
        Assert.Equal(0, _clock.ActiveTimerCount);
    }

    [Fact]
    public void UnlockRequested_WhenThePromptFails_LogsTheFailureTypeOnly()
    {
        var logger = new RecordingLogger<ConnectPageViewModel>();
        using ConnectPageViewModel page = CreatePage(logger);
        _unlock.IsWifiUnlockRequired = true;
        _unlock.OnUnlock = () => throw new InvalidOperationException("Foundry Lab profile failed.");

        _coordinator.RaiseUnlockRequested();

        RecordedLogEntry entry = Assert.Single(logger.Entries, static entry => entry.Level == LogLevel.Warning);
        Assert.Equal("Automatic deployment password prompt failed. FailureType=InvalidOperationException", entry.Message);
        Assert.Null(entry.Exception);
    }

    [Fact]
    public async Task InitializeAsync_AppliesProvisioningOnceThenStartsTheMonitor()
    {
        _coordinator.ProvisioningResult = new ProvisioningOutcome(
            Dot1xState.NotConfigured,
            new ProvisionedWifiState(ProvisionedWifiStatus.Ready, Ssid: WifiSsid.FromText("Foundry Lab")),
            null,
            [NetworkFailureCode.WifiProfileRejected]);

        await _page.InitializeAsync(TestContext.Current.CancellationToken);
        await _page.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, _coordinator.ApplyProvisioningCalls);
        Assert.Equal(1, _monitor.RunCalls);
        Assert.Equal(Text("Wifi.ActionApplyProvisionedFailed"), _page.ProvisionedWifi.FeedbackText);
    }

    [Fact]
    public void RefreshCommand_RequestsAMonitorRefresh()
    {
        _page.RefreshCommand.Execute(null);

        Assert.Equal(1, _monitor.RefreshRequests);
    }

    [Fact]
    public async Task Dispose_DuringTheWiredPrompt_NeverProvisionsNorStartsTheMonitor()
    {
        _unlock.IsWiredUnlockRequired = true;
        _unlock.Result = ConnectUnlockResult.Unlocked;
        _unlock.OnUnlock = _page.Dispose;

        await _page.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal([ConnectUnlockTrigger.WiredStartup], _unlock.Calls);
        Assert.Equal(0, _coordinator.ApplyProvisioningCalls);
        Assert.Equal(0, _monitor.RunCalls);
    }

    [Fact]
    public async Task Dispose_DuringProvisioning_NeverStartsTheMonitor()
    {
        _coordinator.ProvisioningGate = new TaskCompletionSource<ProvisioningOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task initialize = _page.InitializeAsync(TestContext.Current.CancellationToken);

        _page.Dispose();
        _coordinator.ProvisioningGate.TrySetResult(ProvisioningOutcome.NotConfigured);
        await initialize;

        Assert.Equal(0, _monitor.RunCalls);
    }

    [Fact]
    public async Task Dispose_WhileProvisioningIgnoresCancellation_NeverStartsTheMonitor()
    {
        _coordinator.ProvisioningIgnoresCancellation = true;
        _coordinator.ProvisioningGate = new TaskCompletionSource<ProvisioningOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task initialize = _page.InitializeAsync(TestContext.Current.CancellationToken);

        _page.Dispose();
        _coordinator.ProvisioningGate.TrySetResult(new ProvisioningOutcome(
            Dot1xState.NotConfigured,
            new ProvisionedWifiState(ProvisionedWifiStatus.Ready, Ssid: WifiSsid.FromText("Foundry Lab")),
            null,
            [NetworkFailureCode.WifiProfileRejected]));
        await initialize;

        Assert.Equal(0, _monitor.RunCalls);
        Assert.False(_page.ProvisionedWifi.HasFeedback);
    }

    [Fact]
    public async Task Dispose_ObservesAFaultedMonitor()
    {
        var fault = new InvalidOperationException("Monitor loop failed.");
        bool unobserved = false;
        EventHandler<UnobservedTaskExceptionEventArgs> handler = (_, args) => unobserved |= args.Exception.InnerExceptions.Contains(fault);
        TaskScheduler.UnobservedTaskException += handler;
        try
        {
            WeakReference monitorTask = await StartAndDisposeWithFaultedMonitorAsync(fault);

            // The faulted task can only be collected once nothing, the observing continuation included, references it.
            for (int attempt = 0; attempt < 100 && monitorTask.IsAlive; attempt++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            Assert.False(monitorTask.IsAlive);
            Assert.False(unobserved);
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= handler;
        }
    }

    [Fact]
    public async Task Dispose_IsIdempotentAndStopsRendering()
    {
        await _page.InitializeAsync(TestContext.Current.CancellationToken);

        _page.Dispose();
        _page.Dispose();
        Publish(ConnectDebugScenario.EthernetReady);
        await _page.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ConnectPageStatus.Checking, _page.Status);
        Assert.Equal(1, _monitor.RunCalls);
    }

    [Fact]
    public void StateChanged_RendersTheCurrentStateNotTheStaleArgument()
    {
        _coordinator.State = DebugScenarioCatalog.Create(ConnectDebugScenario.EthernetReady, _clock.GetUtcNow());

        _coordinator.RaiseStale(DebugScenarioCatalog.Create(ConnectDebugScenario.NoLink, _clock.GetUtcNow()));

        Assert.Equal(ConnectPageStatus.Ready, _page.Status);
    }

    [Fact]
    public void ApplyDebugScenario_LogsTheScenarioOnly()
    {
        var logger = new RecordingLogger<ConnectPageViewModel>();
        using ConnectPageViewModel page = CreatePage(logger);

        page.ApplyDebugScenario(ConnectDebugScenario.WifiReady);

        RecordedLogEntry entry = Assert.Single(logger.Entries);
        Assert.Equal("Debug scenario applied. Scenario=WifiReady", entry.Message);
    }

    // Not inlined: once it returns, nothing in the test references the page, the monitor or the faulted task.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private async Task<WeakReference> StartAndDisposeWithFaultedMonitorAsync(Exception fault)
    {
        var monitor = new FakeNetworkMonitor { RunResult = Task.FromException(fault) };
        ConnectPageViewModel page = CreatePage(new RecordingLogger<ConnectPageViewModel>(), monitor);
        await page.InitializeAsync(TestContext.Current.CancellationToken);
        var monitorTask = new WeakReference(monitor.RunResult);
        page.Dispose();
        return monitorTask;
    }

    private ConnectPageViewModel CreatePage(RecordingLogger<ConnectPageViewModel> logger, FakeNetworkMonitor? monitor = null)
    {
        var actions = new ConnectNetworkActions(_coordinator, _unlock, new RecordingLogger<ConnectNetworkActions>());
        var dispatcher = new ImmediateUiDispatcher();
        return new ConnectPageViewModel(
            _localization, _coordinator, monitor ?? _monitor, actions,
            new AutoContinueViewModel(_localization, _exit, new FakeApplicationLifetime(), _clock, dispatcher, new RecordingLogger<AutoContinueViewModel>(), isAutoContinueEnabled: false),
            new EthernetSectionViewModel(_localization),
            new ProvisionedWifiViewModel(_localization, actions, new FoundryConnectConfigurationDocument()),
            new WifiNetworksViewModel(_localization, actions, _monitor),
            new DiagnosticsViewModel(_localization, new RecordingLogger<DiagnosticsViewModel>()),
            dispatcher, _clock, logger);
    }
}
