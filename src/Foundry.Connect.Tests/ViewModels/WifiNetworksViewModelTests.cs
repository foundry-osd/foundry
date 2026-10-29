// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Network;
using Foundry.Connect.Tests.Support;
using Foundry.Connect.ViewModels;
using Foundry.Wpf.Localization;

namespace Foundry.Connect.Tests.ViewModels;

public sealed class WifiNetworksViewModelTests : IDisposable
{
    private static readonly Guid Adapter = Guid.Parse("eeeeeeee-0000-0000-0000-000000000001");
    private readonly ResourceLocalizationService _localization = TestLocalization.Create();
    private readonly FakeConnectionCoordinator _coordinator = new();
    private readonly FakeNetworkMonitor _monitor = new();
    private readonly ConnectNetworkActions _actions;
    private readonly WifiNetworksViewModel _viewModel;
    private int _interactions;

    public WifiNetworksViewModelTests()
    {
        _actions = new ConnectNetworkActions(_coordinator, new FakeUnlockService(), new RecordingLogger<ConnectNetworkActions>());
        _actions.InteractionOccurred += (_, _) => _interactions++;
        _viewModel = new WifiNetworksViewModel(_localization, _actions, _monitor);
    }

    public void Dispose() => _viewModel.Dispose();

    private static IReadOnlyList<VisibleWifiNetwork> Networks(params (string Ssid, WifiSecurityType Security, int Signal)[] rows) =>
        rows.Select(row => new VisibleWifiNetwork(Adapter, WifiSsid.FromText(row.Ssid), row.Security, row.Signal)).ToArray();

    private static WifiState State(IReadOnlyList<VisibleWifiNetwork> networks, string? connected = null) =>
        new(WifiAvailability.Available,
            connected is null ? null : new WifiConnection(Adapter, WifiSsid.FromText(connected), connected, WifiSecurityType.Wpa2Personal),
            networks,
            ProvisionedWifiState.NotConfigured);

    private WifiNetworkItemViewModel Item(string name) => _viewModel.Networks.Single(item => item.DisplayName == name);

    [Fact]
    public void Apply_WithoutSelection_FollowsThePolicyOrderAndReportsNoInteraction()
    {
        _viewModel.Apply(State(Networks(("A", WifiSecurityType.Open, 90), ("B", WifiSecurityType.Wpa2Personal, 40))), null);
        _viewModel.Apply(State(Networks(("B", WifiSecurityType.Wpa2Personal, 95), ("A", WifiSecurityType.Open, 90))), null);

        Assert.Equal(["B", "A"], _viewModel.Networks.Select(static item => item.DisplayName));
        Assert.Equal(0, _interactions);
    }

    [Fact]
    public void Apply_WhileANetworkIsSelected_KeepsOrderSelectionAndPassphrase()
    {
        _viewModel.Apply(State(Networks(("A", WifiSecurityType.Open, 90), ("B", WifiSecurityType.Wpa2Personal, 40))), null);
        WifiNetworkItemViewModel selected = Item("B");
        _viewModel.SelectedNetwork = selected;
        _viewModel.SetPassphrase("half typed".AsSpan());
        int cleared = 0;
        _viewModel.PassphraseCleared += (_, _) => cleared++;

        _viewModel.Apply(State(Networks(("C", WifiSecurityType.Open, 99), ("B", WifiSecurityType.Wpa2Personal, 80), ("A", WifiSecurityType.Open, 10))), null);

        Assert.Equal(["A", "B", "C"], _viewModel.Networks.Select(static item => item.DisplayName));
        Assert.Same(selected, _viewModel.SelectedNetwork);
        Assert.True(_viewModel.HasPassphrase);
        Assert.Equal(0, cleared);
    }

    [Fact]
    public void Apply_KeepsTheSelectedNetworkWhenItDisappears()
    {
        _viewModel.Apply(State(Networks(("A", WifiSecurityType.Open, 90), ("B", WifiSecurityType.Wpa2Personal, 40))), null);
        _viewModel.SelectedNetwork = Item("B");
        _viewModel.SetPassphrase("password1".AsSpan());

        _viewModel.Apply(State(Networks(("A", WifiSecurityType.Open, 90))), null);

        Assert.False(Item("B").IsInRange);
        Assert.Equal(0, Item("B").SignalQuality);
        Assert.False(_viewModel.ConnectCommand.CanExecute(null));
        Assert.False(_viewModel.ShowPassphrase);
        Assert.True(_viewModel.HasPassphrase);
    }

    [Fact]
    public void Apply_DistinguishesSsidsByBytes()
    {
        _viewModel.Apply(State(Networks(("Lab", WifiSecurityType.Open, 70), ("LAB", WifiSecurityType.Open, 60)), connected: "LAB"), null);

        Assert.Equal(2, _viewModel.Networks.Count);
        Assert.False(Item("Lab").IsConnected);
        Assert.True(Item("LAB").IsConnected);
    }

    [Fact]
    public void SelectingAnotherNetwork_ClearsThePassphraseAndReportsAnInteraction()
    {
        _viewModel.Apply(State(Networks(("A", WifiSecurityType.Wpa2Personal, 90), ("B", WifiSecurityType.Wpa2Personal, 40))), null);
        _viewModel.SelectedNetwork = Item("A");
        _viewModel.SetPassphrase("password1".AsSpan());
        int cleared = 0;
        _viewModel.PassphraseCleared += (_, _) => cleared++;

        _viewModel.SelectedNetwork = Item("B");

        Assert.False(_viewModel.HasPassphrase);
        Assert.Equal(1, cleared);
        Assert.True(_interactions >= 2);
    }

    [Fact]
    public async Task Connect_PassesTheExactPassphrase()
    {
        _viewModel.Apply(State(Networks(("Workshop", WifiSecurityType.Wpa2Personal, 64))), null);
        _viewModel.SelectedNetwork = Item("Workshop");
        Assert.False(_viewModel.ConnectCommand.CanExecute(null));
        _viewModel.SetPassphrase("  pass phrase  ".AsSpan());

        await _viewModel.ConnectCommand.ExecuteAsync(null);

        (WifiSsid ssid, string? passphrase) = Assert.Single(_coordinator.ConnectNetworkCalls);
        Assert.Equal(WifiSsid.FromText("Workshop"), ssid);
        Assert.Equal("  pass phrase  ", passphrase);
        Assert.False(_viewModel.HasPassphrase);
    }

    [Fact]
    public async Task Connect_OpenNetworkSendsNoPassphrase()
    {
        _viewModel.Apply(State(Networks(("Guest", WifiSecurityType.Open, 82))), null);
        _viewModel.SelectedNetwork = Item("Guest");

        await _viewModel.ConnectCommand.ExecuteAsync(null);

        Assert.Null(Assert.Single(_coordinator.ConnectNetworkCalls).Passphrase);
        Assert.False(_viewModel.ShowPassphrase);
    }

    [Fact]
    public async Task Connect_FailureShowsFeedbackAndKeepsThePassphrase()
    {
        _viewModel.Apply(State(Networks(("Workshop", WifiSecurityType.Wpa2Personal, 64))), null);
        _viewModel.SelectedNetwork = Item("Workshop");
        _viewModel.SetPassphrase("short".AsSpan());
        _coordinator.Outcomes.Enqueue(OperationOutcome.Failed(NetworkOperationKind.ConnectWifiNetwork, NetworkFailureCode.InvalidPassphrase));

        await _viewModel.ConnectCommand.ExecuteAsync(null);

        Assert.Equal(_localization.GetString("Wifi.ActionInvalidPassphrase"), _viewModel.FeedbackText);
        Assert.True(_viewModel.HasPassphrase);
    }

    [Theory]
    [InlineData(WifiSecurityType.Wpa2Enterprise, "Wifi.ProvisionedProfileRequired")]
    [InlineData(WifiSecurityType.Wep, "Wifi.ActionSelectedNotSupported")]
    [InlineData(WifiSecurityType.WpaPersonal, "Wifi.ActionSelectedNotSupported")]
    [InlineData(WifiSecurityType.Unknown, "Wifi.ActionSelectedNotSupported")]
    public void UnsupportedNetwork_IsListedButExplained(WifiSecurityType security, string key)
    {
        _viewModel.Apply(State(Networks(("Corp", security, 30))), null);

        _viewModel.SelectedNetwork = Item("Corp");

        Assert.Equal(_localization.GetString(key), _viewModel.UnsupportedText);
        Assert.False(_viewModel.ShowPassphrase);
        Assert.False(_viewModel.ConnectCommand.CanExecute(null));
    }

    [Fact]
    public void BusyCoordinator_DisablesCommands()
    {
        _viewModel.Apply(State(Networks(("Guest", WifiSecurityType.Open, 82))), NetworkOperationKind.ConnectProvisionedWifi);
        _viewModel.SelectedNetwork = Item("Guest");

        Assert.False(_viewModel.ConnectCommand.CanExecute(null));
    }

    [Fact]
    public void Refresh_RequestsAMonitorRefreshWithoutAnInteraction()
    {
        _viewModel.RefreshCommand.Execute(null);

        Assert.Equal(1, _monitor.RefreshRequests);
        Assert.Equal(0, _interactions);
    }

    [Fact]
    public void Item_AutomationNameDescribesTheRow()
    {
        _viewModel.Apply(State(Networks(("Lab", WifiSecurityType.Wpa3Personal, 48)), connected: "Lab"), null);

        WifiNetworkItemViewModel item = Item("Lab");
        Assert.Equal(_localization.Format("Wifi.NetworkConnectedAutomationFormat", "Lab", _localization.GetString("Wifi.SecurityWpa3Personal"), 48), item.AutomationName);
    }

    [Theory]
    [InlineData(OperationStatus.Succeeded)]
    [InlineData(OperationStatus.Failed)]
    public async Task Connect_WhenTheSelectionChangesDuringTheCall_LeavesTheNewSelectionAlone(OperationStatus status)
    {
        _viewModel.Apply(State(Networks(("A", WifiSecurityType.Wpa2Personal, 90), ("B", WifiSecurityType.Wpa2Personal, 40))), null);
        _viewModel.SelectedNetwork = Item("A");
        _viewModel.SetPassphrase("password1".AsSpan());
        _coordinator.OperationGate = new TaskCompletionSource<OperationOutcome>();
        Task connect = _viewModel.ConnectCommand.ExecuteAsync(null);
        _viewModel.SelectedNetwork = Item("B");
        _viewModel.SetPassphrase("password2".AsSpan());
        int cleared = 0;
        _viewModel.PassphraseCleared += (_, _) => cleared++;

        _coordinator.OperationGate.SetResult(status == OperationStatus.Succeeded
            ? OperationOutcome.Success(NetworkOperationKind.ConnectWifiNetwork)
            : OperationOutcome.Failed(NetworkOperationKind.ConnectWifiNetwork, NetworkFailureCode.InvalidPassphrase));
        await connect;

        Assert.Same(Item("B"), _viewModel.SelectedNetwork);
        Assert.True(_viewModel.HasPassphrase);
        Assert.Equal(0, cleared);
        Assert.False(_viewModel.HasFeedback);
    }

    [Fact]
    public void Apply_WhenTheSelectedNetworkBecomesConnected_ZeroesThePassphrase()
    {
        _viewModel.Apply(State(Networks(("Lab", WifiSecurityType.Wpa2Personal, 70))), null);
        _viewModel.SelectedNetwork = Item("Lab");
        _viewModel.SetPassphrase("password1".AsSpan());
        int cleared = 0;
        _viewModel.PassphraseCleared += (_, _) => cleared++;

        _viewModel.Apply(State(Networks(("Lab", WifiSecurityType.Wpa2Personal, 70)), connected: "Lab"), null);

        Assert.False(_viewModel.HasPassphrase);
        Assert.Equal(1, cleared);
    }

    [Fact]
    public async Task SelectingAnotherNetwork_ClearsTheFeedback()
    {
        _viewModel.Apply(State(Networks(("A", WifiSecurityType.Wpa2Personal, 90), ("B", WifiSecurityType.Open, 40))), null);
        _viewModel.SelectedNetwork = Item("A");
        _viewModel.SetPassphrase("short".AsSpan());
        _coordinator.Outcomes.Enqueue(OperationOutcome.Failed(NetworkOperationKind.ConnectWifiNetwork, NetworkFailureCode.InvalidPassphrase));
        await _viewModel.ConnectCommand.ExecuteAsync(null);
        Assert.True(_viewModel.HasFeedback);

        _viewModel.SelectedNetwork = Item("B");

        Assert.False(_viewModel.HasFeedback);
        Assert.Equal(string.Empty, _viewModel.FeedbackText);
    }

    [Fact]
    public async Task Disconnect_ConnectedSelection_DisconnectsWithoutFeedback()
    {
        _viewModel.Apply(State(Networks(("Lab", WifiSecurityType.Open, 70), ("Guest", WifiSecurityType.Open, 50)), connected: "Lab"), null);
        _viewModel.SelectedNetwork = Item("Guest");
        Assert.False(_viewModel.DisconnectCommand.CanExecute(null));
        _viewModel.SelectedNetwork = Item("Lab");
        Assert.True(_viewModel.DisconnectCommand.CanExecute(null));
        Assert.False(_viewModel.ConnectCommand.CanExecute(null));
        int interactions = _interactions;

        await _viewModel.DisconnectCommand.ExecuteAsync(null);

        Assert.Equal(1, _coordinator.DisconnectCalls);
        Assert.False(_viewModel.HasFeedback);
        Assert.Equal(interactions + 1, _interactions);
    }

    [Fact]
    public void Disconnect_IsDisabledWhileAnOperationRuns()
    {
        _viewModel.Apply(State(Networks(("Lab", WifiSecurityType.Open, 70)), connected: "Lab"), NetworkOperationKind.ConnectProvisionedWifi);

        _viewModel.SelectedNetwork = Item("Lab");

        Assert.False(_viewModel.DisconnectCommand.CanExecute(null));
    }

    [Fact]
    public void ChooseNetwork_SelectsTheRowOrReportsAnInteractionForTheSelectedOne()
    {
        _viewModel.Apply(State(Networks(("A", WifiSecurityType.Open, 90), ("B", WifiSecurityType.Open, 40))), null);

        _viewModel.ChooseNetwork(Item("A"));
        Assert.Same(Item("A"), _viewModel.SelectedNetwork);
        int interactions = _interactions;
        _viewModel.ChooseNetwork(Item("A"));

        Assert.Same(Item("A"), _viewModel.SelectedNetwork);
        Assert.Equal(interactions + 1, _interactions);
    }

    [Fact]
    public void ConnectingToAListedNetwork_ShowsTheConnectingCaption()
    {
        _viewModel.Apply(State(Networks(("Lab", WifiSecurityType.Open, 70))), NetworkOperationKind.ConnectWifiNetwork);

        Assert.True(_viewModel.IsConnecting);
        Assert.Equal(_localization.GetString("Status.ConnectingTitle"), _viewModel.ConnectingText);
    }
}
