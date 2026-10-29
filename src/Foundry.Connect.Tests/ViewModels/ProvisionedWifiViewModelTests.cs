// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Network;
using Foundry.Connect.Security;
using Foundry.Connect.Tests.Support;
using Foundry.Connect.ViewModels;
using Foundry.Core.Models.Configuration;
using Foundry.Wpf.Localization;

namespace Foundry.Connect.Tests.ViewModels;

public sealed class ProvisionedWifiViewModelTests : IDisposable
{
    private static readonly Guid Adapter = Guid.Parse("dddddddd-0000-0000-0000-000000000001");
    private static readonly WifiSsid Lab = WifiSsid.FromText("Foundry Lab");
    private readonly ResourceLocalizationService _localization = TestLocalization.Create();
    private readonly FakeConnectionCoordinator _coordinator = new();
    private readonly FakeUnlockService _unlock = new();
    private readonly List<ProvisionedWifiViewModel> _created = [];

    public void Dispose()
    {
        foreach (ProvisionedWifiViewModel viewModel in _created)
        {
            viewModel.Dispose();
        }
    }

    private ConnectNetworkActions Actions() => new(_coordinator, _unlock, new RecordingLogger<ConnectNetworkActions>());

    private ProvisionedWifiViewModel Create(string? securityType = "WPA3-Personal", ConnectNetworkActions? actions = null)
    {
        var viewModel = new ProvisionedWifiViewModel(_localization, actions ?? Actions(), new FoundryConnectConfigurationDocument
        {
            Capabilities = new ConnectNetworkCapabilitiesSettings { WifiProvisioned = true },
            Wifi = new ConnectWifiSettings { IsEnabled = true, Ssid = "Foundry Lab", SecurityType = securityType }
        });
        _created.Add(viewModel);
        return viewModel;
    }

    private static ProvisionedWifiState Ready(bool hidden = false) => new(ProvisionedWifiStatus.Ready, Ssid: Lab, ProfileName: "Foundry Lab", IsHidden: hidden);

    private static WifiConnection ProvisionedConnection => new(Adapter, Lab, "Foundry Lab", WifiSecurityType.Wpa3Personal, WifiConnectionSource.Provisioned);

    private static WifiState Wifi(WifiAvailability availability, ProvisionedWifiState provisioned, WifiConnection? connection = null, bool labVisible = true) =>
        new(availability, connection, labVisible ? [new VisibleWifiNetwork(Adapter, Lab, WifiSecurityType.Wpa3Personal, 70)] : [], provisioned);

    [Theory]
    [InlineData(ProvisionedWifiStatus.NotConfigured, WifiAvailability.Available, false)]
    [InlineData(ProvisionedWifiStatus.Ready, WifiAvailability.Available, true)]
    [InlineData(ProvisionedWifiStatus.Unavailable, WifiAvailability.Available, true)]
    [InlineData(ProvisionedWifiStatus.Ready, WifiAvailability.RuntimeMissing, false)]
    [InlineData(ProvisionedWifiStatus.Ready, WifiAvailability.NoAdapter, false)]
    [InlineData(ProvisionedWifiStatus.Ready, WifiAvailability.Disabled, false)]
    public void IsVisible_OnlyWithAProfileAndWorkingWifi(ProvisionedWifiStatus status, WifiAvailability availability, bool expected)
    {
        ProvisionedWifiViewModel viewModel = Create();

        viewModel.Apply(Wifi(availability, new ProvisionedWifiState(status, Ssid: Lab)), null, null);

        Assert.Equal(expected, viewModel.IsVisible);
    }

    [Theory]
    [InlineData(ProvisionedWifiUnavailableReason.SecretUnavailable, "Wifi.ProvisionedUnavailableSecret")]
    [InlineData(ProvisionedWifiUnavailableReason.ProfileTemplateMissing, "Wifi.ProvisionedUnavailableTemplate")]
    [InlineData(ProvisionedWifiUnavailableReason.InvalidConfiguration, "Wifi.ProvisionedUnavailableConfiguration")]
    [InlineData(ProvisionedWifiUnavailableReason.InvalidPassphrase, "Wifi.ProvisionedUnavailablePassphrase")]
    public void Unavailable_ExplainsWhyAndCannotConnect(ProvisionedWifiUnavailableReason reason, string key)
    {
        ProvisionedWifiViewModel viewModel = Create();

        viewModel.Apply(Wifi(WifiAvailability.Available, new ProvisionedWifiState(ProvisionedWifiStatus.Unavailable, reason, Lab)), null, null);

        Assert.True(viewModel.IsUnavailable);
        Assert.Equal(_localization.GetString(key), viewModel.UnavailableText);
        Assert.Equal(_localization.GetString("Wifi.ProvisionedUnavailableTitle"), viewModel.StatusText);
        Assert.False(viewModel.ConnectCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(ProvisionedWifiUnavailableReason.None, "Wifi.ProvisionedLocked")]
    [InlineData(ProvisionedWifiUnavailableReason.UnlockCancelled, "Wifi.ProvisionedUnlockCancelled")]
    public void Apply_Locked_ShowsTheLockedTextAndKeepsConnectEnabled(ProvisionedWifiUnavailableReason reason, string key)
    {
        ProvisionedWifiViewModel viewModel = Create();

        viewModel.Apply(Wifi(WifiAvailability.Available, new ProvisionedWifiState(ProvisionedWifiStatus.Locked, reason, Lab)), null, null);

        Assert.True(viewModel.IsVisible);
        Assert.True(viewModel.IsLocked);
        Assert.False(viewModel.IsUnavailable);
        Assert.Equal(_localization.GetString("Wifi.StatusLocked"), viewModel.StatusText);
        Assert.Equal(_localization.GetString(key), viewModel.LockedText);
        Assert.True(viewModel.ConnectCommand.CanExecute(null));
    }

    [Fact]
    public async Task Connect_OnALockedProfileThatStaysLocked_ShowsNoFeedback()
    {
        _unlock.IsWifiUnlockRequired = true;
        _unlock.Result = ConnectUnlockResult.Cancelled;
        ProvisionedWifiViewModel viewModel = Create();
        bool canExecuteWhilePrompting = true;
        _unlock.OnUnlock = () => canExecuteWhilePrompting = viewModel.ConnectCommand.CanExecute(null);
        _coordinator.Publish(_coordinator.State with
        {
            Wifi = Wifi(WifiAvailability.Available, new ProvisionedWifiState(ProvisionedWifiStatus.Locked, Ssid: Lab))
        });
        viewModel.Apply(_coordinator.State.Wifi, null, null);

        await viewModel.ConnectCommand.ExecuteAsync(null);

        Assert.Equal([ConnectUnlockTrigger.ManualWifi], _unlock.Calls);
        Assert.False(canExecuteWhilePrompting);
        Assert.Equal(0, _coordinator.ConnectProvisionedCalls);
        Assert.False(viewModel.HasFeedback);
    }

    public static TheoryData<string, WifiState, NetworkOperationKind?, string> StatusRows => new()
    {
        { "ready", Wifi(WifiAvailability.Available, Ready()), null, "Wifi.StatusReadyToConnect" },
        { "not_in_range", Wifi(WifiAvailability.Available, Ready(), labVisible: false), null, "Wifi.StatusNotInRange" },
        { "hidden_profile", Wifi(WifiAvailability.Available, Ready(hidden: true), labVisible: false), null, "Wifi.StatusReadyToConnect" },
        { "enterprise_without_ssid", Wifi(WifiAvailability.Available, new ProvisionedWifiState(ProvisionedWifiStatus.Ready, ProfileName: "Corp", IsEnterprise: true), labVisible: false), null, "Wifi.StatusReadyToConnect" },
        { "connected", Wifi(WifiAvailability.Available, Ready(), ProvisionedConnection), null, "Common.Connected" },
        { "other_network", Wifi(WifiAvailability.Available, Ready(), new WifiConnection(Adapter, WifiSsid.FromText("Guest"), "Guest", WifiSecurityType.Open)), null, "Wifi.StatusAnotherNetworkActive" },
        { "connecting", Wifi(WifiAvailability.Available, Ready()), NetworkOperationKind.ConnectProvisionedWifi, "Status.ConnectingTitle" }
    };

    [Theory]
    [MemberData(nameof(StatusRows))]
    public void StatusText_ScenarioTable(string scenario, WifiState wifi, NetworkOperationKind? operation, string key)
    {
        ProvisionedWifiViewModel viewModel = Create();

        viewModel.Apply(wifi, operation, null);

        Assert.True(_localization.GetString(key) == viewModel.StatusText, scenario);
    }

    [Fact]
    public void DisplayName_PrefersTheSsidThenTheProfileNameThenAGenericLabel()
    {
        ProvisionedWifiViewModel viewModel = Create();

        viewModel.Apply(Wifi(WifiAvailability.Available, Ready()), null, null);
        Assert.Equal("Foundry Lab", viewModel.DisplayName);

        viewModel.Apply(Wifi(WifiAvailability.Available, new ProvisionedWifiState(ProvisionedWifiStatus.Ready, ProfileName: "Corp", IsEnterprise: true)), null, null);
        Assert.Equal("Corp", viewModel.DisplayName);

        viewModel.Apply(Wifi(WifiAvailability.Available, new ProvisionedWifiState(ProvisionedWifiStatus.Ready, IsEnterprise: true)), null, null);
        Assert.Equal(_localization.GetString("Wifi.UnnamedProvisionedProfile"), viewModel.DisplayName);
    }

    [Theory]
    [InlineData("WPA3-Personal", false, "Wifi.SecurityWpa3Personal")]
    [InlineData("WPA2/WPA3-Personal", false, "Wifi.SecurityWpa2Wpa3Personal")]
    [InlineData("Unknown", false, "Wifi.ConfiguredProfile")]
    [InlineData("WPA2/WPA3-Enterprise", true, "Wifi.SecurityEnterprise")]
    public void SecurityText_ComesFromTheConfiguration(string securityType, bool enterprise, string key)
    {
        ProvisionedWifiViewModel viewModel = Create(securityType);

        viewModel.Apply(Wifi(WifiAvailability.Available, Ready() with { IsEnterprise = enterprise }), null, null);

        Assert.Equal(_localization.GetString(key), viewModel.SecurityText);
    }

    [Fact]
    public async Task Connect_UsesTheCoordinatorAndShowsFailures()
    {
        ProvisionedWifiViewModel viewModel = Create();
        viewModel.Apply(Wifi(WifiAvailability.Available, Ready()), null, null);
        _coordinator.Outcomes.Enqueue(OperationOutcome.Failed(NetworkOperationKind.ConnectProvisionedWifi, NetworkFailureCode.ConnectTimedOut));

        await viewModel.ConnectCommand.ExecuteAsync(null);

        Assert.Equal(1, _coordinator.ConnectProvisionedCalls);
        Assert.Equal(_localization.GetString("Wifi.ActionConnectTimedOut"), viewModel.FeedbackText);
    }

    [Fact]
    public async Task Connect_SuccessClearsEarlierFeedback()
    {
        ProvisionedWifiViewModel viewModel = Create();
        viewModel.Apply(Wifi(WifiAvailability.Available, Ready()), null, null);
        _coordinator.Outcomes.Enqueue(OperationOutcome.Failed(NetworkOperationKind.ConnectProvisionedWifi, NetworkFailureCode.ConnectTimedOut));
        await viewModel.ConnectCommand.ExecuteAsync(null);
        Assert.True(viewModel.HasFeedback);

        await viewModel.ConnectCommand.ExecuteAsync(null);

        Assert.False(viewModel.HasFeedback);
        Assert.Equal(string.Empty, viewModel.FeedbackText);
    }

    [Fact]
    public async Task Disconnect_FailureUsesTheProvisionedText()
    {
        ProvisionedWifiViewModel viewModel = Create();
        viewModel.Apply(Wifi(WifiAvailability.Available, Ready(), ProvisionedConnection), null, null);
        _coordinator.Outcomes.Enqueue(OperationOutcome.Failed(NetworkOperationKind.DisconnectWifi, NetworkFailureCode.DisconnectFailed));

        await viewModel.DisconnectCommand.ExecuteAsync(null);

        Assert.Equal(_localization.GetString("Wifi.ActionProvisionedDisconnectFailed"), viewModel.FeedbackText);
    }

    [Fact]
    public async Task Simulation_ConnectNeverReachesTheCoordinator()
    {
        ConnectNetworkActions actions = Actions();
        actions.IsSimulation = true;
        ProvisionedWifiViewModel viewModel = Create(actions: actions);
        viewModel.Apply(Wifi(WifiAvailability.Available, Ready()), null, null);

        await viewModel.ConnectCommand.ExecuteAsync(null);

        Assert.Equal(0, _coordinator.ConnectProvisionedCalls);
        Assert.False(viewModel.HasFeedback);
    }

    [Fact]
    public async Task Commands_ReportANetworkingInteraction()
    {
        ConnectNetworkActions actions = Actions();
        int interactions = 0;
        actions.InteractionOccurred += (_, _) => interactions++;
        ProvisionedWifiViewModel viewModel = Create(actions: actions);
        viewModel.Apply(Wifi(WifiAvailability.Available, Ready()), null, null);

        await viewModel.ConnectCommand.ExecuteAsync(null);

        Assert.Equal(1, interactions);
    }

    [Fact]
    public void Commands_FollowTheConnectionState()
    {
        ProvisionedWifiViewModel viewModel = Create();

        viewModel.Apply(Wifi(WifiAvailability.Available, Ready()), null, null);
        Assert.True(viewModel.ConnectCommand.CanExecute(null));
        Assert.False(viewModel.DisconnectCommand.CanExecute(null));

        viewModel.Apply(Wifi(WifiAvailability.Available, Ready(), ProvisionedConnection), null, null);
        Assert.False(viewModel.ConnectCommand.CanExecute(null));
        Assert.True(viewModel.DisconnectCommand.CanExecute(null));

        viewModel.Apply(Wifi(WifiAvailability.Available, Ready(), new WifiConnection(Adapter, WifiSsid.FromText("Guest"), "Guest", WifiSecurityType.Open)), null, null);
        Assert.True(viewModel.ConnectCommand.CanExecute(null));
        Assert.False(viewModel.DisconnectCommand.CanExecute(null));
    }

    [Fact]
    public void AutomaticOutcome_ShowsFeedbackUntilTheNextSuccess()
    {
        ProvisionedWifiViewModel viewModel = Create();

        viewModel.Apply(Wifi(WifiAvailability.Available, Ready()), null, OperationOutcome.Failed(NetworkOperationKind.ConnectProvisionedWifi, NetworkFailureCode.ConnectionFailed));
        Assert.Equal(_localization.GetString("Wifi.ActionProvisionedConnectFailed"), viewModel.FeedbackText);

        viewModel.Apply(Wifi(WifiAvailability.Available, Ready()), null, OperationOutcome.Success(NetworkOperationKind.ConnectProvisionedWifi));
        Assert.False(viewModel.HasFeedback);
    }

    [Fact]
    public void AutomaticOutcome_ASecondIdenticalFailureCountsAsNew()
    {
        ProvisionedWifiViewModel viewModel = Create();
        OperationOutcome first = OperationOutcome.Failed(NetworkOperationKind.ConnectProvisionedWifi, NetworkFailureCode.ConnectionFailed);
        OperationOutcome second = OperationOutcome.Failed(NetworkOperationKind.ConnectProvisionedWifi, NetworkFailureCode.ConnectionFailed);
        Assert.Equal(first, second);
        Assert.NotSame(first, second);

        viewModel.Apply(Wifi(WifiAvailability.Available, Ready()), null, first);
        viewModel.Apply(Wifi(WifiAvailability.Available, Ready()), null, OperationOutcome.Success(NetworkOperationKind.ConnectProvisionedWifi));
        viewModel.Apply(Wifi(WifiAvailability.Available, Ready()), null, second);

        Assert.True(viewModel.HasFeedback);
    }

    [Fact]
    public async Task AutomaticOutcome_TheSameInstanceDoesNotOverwriteTheCommandResult()
    {
        ProvisionedWifiViewModel viewModel = Create();
        OperationOutcome failure = OperationOutcome.Failed(NetworkOperationKind.ConnectProvisionedWifi, NetworkFailureCode.ConnectionFailed);
        viewModel.Apply(Wifi(WifiAvailability.Available, Ready()), null, failure);
        _coordinator.Outcomes.Enqueue(OperationOutcome.Success(NetworkOperationKind.ConnectProvisionedWifi));
        await viewModel.ConnectCommand.ExecuteAsync(null);
        Assert.False(viewModel.HasFeedback);

        viewModel.Apply(Wifi(WifiAvailability.Available, Ready()), null, failure);

        Assert.False(viewModel.HasFeedback);
    }

    [Fact]
    public void AutomaticOutcome_OtherOperationsAreIgnored()
    {
        ProvisionedWifiViewModel viewModel = Create();

        viewModel.Apply(Wifi(WifiAvailability.Available, Ready()), null, OperationOutcome.Failed(NetworkOperationKind.ConnectWifiNetwork, NetworkFailureCode.ConnectionFailed));

        Assert.False(viewModel.HasFeedback);
    }

    [Fact]
    public void BusyCoordinator_DisablesBothCommands()
    {
        ProvisionedWifiViewModel viewModel = Create();

        viewModel.Apply(Wifi(WifiAvailability.Available, Ready()), NetworkOperationKind.ConnectWifiNetwork, null);

        Assert.False(viewModel.ConnectCommand.CanExecute(null));
        Assert.False(viewModel.DisconnectCommand.CanExecute(null));
    }

    [Fact]
    public void ProvisioningFailure_ShowsTheApplyText()
    {
        ProvisionedWifiViewModel viewModel = Create();

        viewModel.ApplyProvisioningOutcome(new ProvisioningOutcome(Dot1xState.NotConfigured, Ready(), null, [NetworkFailureCode.WifiProfileRejected]));

        Assert.Equal(_localization.GetString("Wifi.ActionApplyProvisionedFailed"), viewModel.FeedbackText);
    }

    [Fact]
    public void BecomingConnected_ClearsAnEarlierFailure()
    {
        ProvisionedWifiViewModel viewModel = Create();
        viewModel.ApplyProvisioningOutcome(new ProvisioningOutcome(Dot1xState.NotConfigured, Ready(), null, [NetworkFailureCode.WifiProfileRejected]));
        viewModel.Apply(Wifi(WifiAvailability.Available, Ready()), null, null);
        Assert.True(viewModel.HasFeedback);

        viewModel.Apply(Wifi(WifiAvailability.Available, Ready(), ProvisionedConnection), null, null);

        Assert.False(viewModel.HasFeedback);
    }

    [Fact]
    public async Task StayingConnected_KeepsADisconnectFailure()
    {
        ProvisionedWifiViewModel viewModel = Create();
        viewModel.Apply(Wifi(WifiAvailability.Available, Ready(), ProvisionedConnection), null, null);
        _coordinator.Outcomes.Enqueue(OperationOutcome.Failed(NetworkOperationKind.DisconnectWifi, NetworkFailureCode.DisconnectFailed));
        await viewModel.DisconnectCommand.ExecuteAsync(null);

        viewModel.Apply(Wifi(WifiAvailability.Available, Ready(), ProvisionedConnection), null, null);

        Assert.True(viewModel.HasFeedback);
    }

    [Fact]
    public void ProvisioningFailure_FromTheWiredPartShowsNoWifiText()
    {
        ProvisionedWifiViewModel viewModel = Create();

        viewModel.ApplyProvisioningOutcome(new ProvisioningOutcome(new Dot1xState(Dot1xAuthenticationStatus.Failed, Dot1xFailureReason.ProfileImportFailed), Ready(), null, [NetworkFailureCode.Unexpected]));

        Assert.False(viewModel.HasFeedback);
    }

    [Fact]
    public void RefreshTexts_RaisesTheTextsAgainFromTheTypedState()
    {
        ProvisionedWifiViewModel viewModel = Create();
        viewModel.Apply(Wifi(WifiAvailability.Available, Ready()), null, OperationOutcome.Failed(NetworkOperationKind.ConnectProvisionedWifi, NetworkFailureCode.ConnectTimedOut));
        List<string?> raised = [];
        viewModel.PropertyChanged += (_, args) => raised.Add(args.PropertyName);

        viewModel.RefreshTexts();

        Assert.Contains(nameof(ProvisionedWifiViewModel.FeedbackText), raised);
        Assert.Contains(nameof(ProvisionedWifiViewModel.StatusText), raised);
        Assert.Equal(_localization.GetString("Wifi.ActionConnectTimedOut"), viewModel.FeedbackText);
    }

    [Fact]
    public async Task Disposed_CommandIsANoOp()
    {
        ProvisionedWifiViewModel viewModel = Create();
        viewModel.Apply(Wifi(WifiAvailability.Available, Ready()), null, null);
        viewModel.Dispose();

        await viewModel.ConnectCommand.ExecuteAsync(null);

        Assert.Equal(0, _coordinator.ConnectProvisionedCalls);
    }

    [Fact]
    public async Task RunningCommand_DisablesBothCommandsUntilItEnds()
    {
        ProvisionedWifiViewModel viewModel = Create();
        viewModel.Apply(Wifi(WifiAvailability.Available, Ready()), null, null);
        _coordinator.OperationGate = new TaskCompletionSource<OperationOutcome>();

        Task connect = viewModel.ConnectCommand.ExecuteAsync(null);

        // The profile connects before the command returns: Disconnect stays disabled while the command runs.
        viewModel.Apply(Wifi(WifiAvailability.Available, Ready(), ProvisionedConnection), null, null);
        Assert.False(viewModel.ConnectCommand.CanExecute(null));
        Assert.False(viewModel.DisconnectCommand.CanExecute(null));
        _coordinator.OperationGate.SetResult(OperationOutcome.Success(NetworkOperationKind.ConnectProvisionedWifi));
        await connect;
        Assert.True(viewModel.DisconnectCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("throws")]
    [InlineData("cancelled")]
    [InlineData("cancelled-outcome")]
    public async Task RunningCommand_ThatThrowsOrIsCancelled_ReenablesTheCommands(string ending)
    {
        ProvisionedWifiViewModel viewModel = Create();
        viewModel.Apply(Wifi(WifiAvailability.Available, Ready()), null, null);
        _coordinator.OperationGate = new TaskCompletionSource<OperationOutcome>();
        Task connect = viewModel.ConnectCommand.ExecuteAsync(null);
        Assert.False(viewModel.ConnectCommand.CanExecute(null));

        switch (ending)
        {
            case "throws":
                _coordinator.OperationGate.SetException(new InvalidOperationException("Simulated failure."));
                await Assert.ThrowsAsync<InvalidOperationException>(() => connect);
                break;
            case "cancelled":
                _coordinator.OperationGate.SetCanceled(TestContext.Current.CancellationToken);
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connect);
                break;
            default:
                _coordinator.OperationGate.SetResult(OperationOutcome.Cancelled(NetworkOperationKind.ConnectProvisionedWifi));
                await connect;
                break;
        }

        Assert.True(viewModel.ConnectCommand.CanExecute(null));
    }
}
