// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using Foundry.Core.Models.Configuration;
using Foundry.Connect.Tests.Support;
using Foundry.Core.Models.Runtime;
using Foundry.Core.Services.Runtime;
using Foundry.Wpf.Shell;
using Foundry.Connect.Configuration;
using Foundry.Wpf.Theme;
using Foundry.Connect.ViewModels;
using Foundry.Utilities.Globalization;
using Foundry.Wpf.Localization;
using Foundry.Connect.Network;
using Foundry.Wpf.Network;

namespace Foundry.Connect.Tests;

[Collection(nameof(ProcessCultureCollection))]
public sealed class MainWindowViewModelTests
{
    private readonly ManualTimeProvider _clock = new();
    private readonly FakeConnectionCoordinator _coordinator = new();
    private readonly FakeNetworkMonitor _monitor = new();
    private readonly FakeSuccessfulExitService _exit = new();
    private readonly FakeApplicationLifetime _lifetime = new();
    private readonly ResourceLocalizationService _localization = TestLocalization.Create();

    [Theory]
    [InlineData(BootMediaUpdateReason.NewerRelease, "BootMedia.UpdateAvailable", "BootMedia.UpdateRecommendedToolTip")]
    [InlineData(BootMediaUpdateReason.UnknownAuthoringVersion, "BootMedia.UpdateRecommended", "BootMedia.UnknownAuthoringVersionToolTip")]
    public void BootMediaBanner_SelectsCaptionAndToolTipForReason(BootMediaUpdateReason reason, string textKey, string toolTipKey)
    {
        using MainWindowViewModel viewModel = CreateViewModel(bootMediaUpdateReason: reason);

        Assert.True(viewModel.IsBootMediaUpdateRecommended);
        Assert.Equal(viewModel.Strings[textKey], viewModel.BootMediaUpdateRecommendedText);
        Assert.Equal(viewModel.Strings[toolTipKey], viewModel.BootMediaUpdateRecommendedToolTip);
    }

    [Fact]
    public void KeyboardMenu_ListsActiveLanguageFirst()
    {
        using MainWindowViewModel viewModel = CreateViewModel(keyboardLayoutSwitcher: new FakeKeyboardLayoutSwitcher("0000040C"));

        Assert.Equal(["0000080C", "0000040C", null, null], viewModel.KeyboardMenuEntries.Select(entry => entry.LayoutId));
        Assert.True(viewModel.KeyboardMenuEntries[1].IsSelected);
        Assert.True(viewModel.KeyboardMenuEntries[2].IsSeparator);
        KeyboardMenuEntry dutch = Assert.Single(viewModel.KeyboardMenuEntries[3].Children);
        Assert.Equal(["00000413"], dutch.Children.Select(entry => entry.LayoutId));
        Assert.Contains("FR", viewModel.KeyboardMenuHeader, StringComparison.Ordinal);
    }

    [Fact]
    public void SetKeyboardLayoutCommand_AppliesAndRefreshesSelection()
    {
        var switcher = new FakeKeyboardLayoutSwitcher("0000040C");
        using MainWindowViewModel viewModel = CreateViewModel(keyboardLayoutSwitcher: switcher);

        viewModel.SetKeyboardLayoutCommand.Execute("0000080C");

        Assert.Equal(["0000080C"], switcher.AppliedLayoutIds);
        Assert.True(viewModel.KeyboardMenuEntries.Single(entry => entry.LayoutId == "0000080C").IsSelected);
        Assert.Contains("BE", viewModel.KeyboardMenuHeader, StringComparison.Ordinal);
    }

    [Fact]
    public void SetKeyboardLayoutCommand_WhenApplyFails_KeepsPreviousSelection()
    {
        var switcher = new FakeKeyboardLayoutSwitcher("0000040C") { FailApply = true };
        using MainWindowViewModel viewModel = CreateViewModel(keyboardLayoutSwitcher: switcher);

        viewModel.SetKeyboardLayoutCommand.Execute("0000080C");

        Assert.True(viewModel.KeyboardMenuEntries.Single(entry => entry.LayoutId == "0000040C").IsSelected);
        Assert.Contains("FR", viewModel.KeyboardMenuHeader, StringComparison.Ordinal);
    }

    [Fact]
    public void DebugMenu_IsHiddenOutsideDebugSafetyMode()
    {
        using MainWindowViewModel viewModel = CreateViewModel();   // public constructor; Release tests → DebugSafetyMode.IsEnabled is false

        Assert.False(viewModel.IsDebugMenuVisible);
        Assert.False(viewModel.SetDebugScenarioCommand.CanExecute(ConnectDebugScenario.CaptivePortal));

        // The command also guards itself: executing it anyway never reaches the page.
        viewModel.SetDebugScenarioCommand.Execute(ConnectDebugScenario.CaptivePortal);
        Assert.Equal(ConnectDebugScenario.Live, viewModel.Page.ActiveDebugScenario);
    }

    [Fact]
    public void SetDebugScenario_AppliesItToThePageAndChecksTheMenu()
    {
        using MainWindowViewModel viewModel = CreateViewModel(isDebugSafeMode: true);

        viewModel.SetDebugScenarioCommand.Execute(ConnectDebugScenario.CaptivePortal);

        Assert.Equal(ConnectDebugScenario.CaptivePortal, viewModel.Page.ActiveDebugScenario);
        Assert.Equal(BlockedReason.CaptivePortal, viewModel.Page.BlockedReason);
        Assert.Equal(ConnectDebugScenario.CaptivePortal, Assert.Single(viewModel.DebugScenarios, static option => option.IsSelected).Scenario);
        Assert.Equal(19, viewModel.DebugScenarios.Count);
    }

    [Fact]
    public void LanguageChange_RebuildsTheScenarioHeaders()
    {
        CultureInfo original = CultureInfo.CurrentUICulture;
        using MainWindowViewModel viewModel = CreateViewModel(isDebugSafeMode: true);
        viewModel.SetDebugScenarioCommand.Execute(ConnectDebugScenario.WifiReady);

        try
        {
            _localization.SetCulture(CultureInfo.GetCultureInfo(original.Name == "fr-FR" ? "de-DE" : "fr-FR"));

            Assert.Equal(
                DebugScenarioCatalog.All.Select(scenario => _localization.GetString(DebugScenarioCatalog.ResourceKey(scenario))),
                viewModel.DebugScenarios.Select(option => option.Header));
            Assert.Equal(ConnectDebugScenario.WifiReady, Assert.Single(viewModel.DebugScenarios, static option => option.IsSelected).Scenario);
        }
        finally
        {
            _localization.SetCulture(original);
        }
    }

    [Fact]
    public void ApplyInitialPreferences_AppliesTheDeploymentThemeAndLanguageAndKeepsThemOverUnknownValues()
    {
        CultureInfo original = CultureInfo.CurrentUICulture;
        string target = original.Name == "fr-FR" ? "de-DE" : "fr-FR";
        using MainWindowViewModel viewModel = CreateViewModel();

        try
        {
            viewModel.ApplyInitialPreferences("Dark", target);

            Assert.Equal(ThemeMode.Dark, viewModel.CurrentTheme);
            Assert.Equal(target, viewModel.CurrentCulture.Name);

            viewModel.ApplyInitialPreferences("Sepia", "xx-NOTACULTURE");

            Assert.Equal(ThemeMode.Dark, viewModel.CurrentTheme);
            Assert.Equal(target, viewModel.CurrentCulture.Name);
        }
        finally
        {
            _localization.SetCulture(original);
        }
    }

    [Fact]
    public void HandleWindowClosing_RequestsUserAbortedUnlessAnExitWon()
    {
        using MainWindowViewModel viewModel = CreateViewModel();

        viewModel.HandleWindowClosing();
        viewModel.HandleWindowClosing();

        Assert.Equal([ChildRuntimeExitCode.UserAborted], _lifetime.Codes);
    }

    [Fact]
    public void NetworkIndicator_StartsDisconnectedWithItsToolTip()
    {
        using MainWindowViewModel viewModel = CreateViewModel();

        Assert.Equal(NetworkIndicatorState.Disconnected, viewModel.NetworkIndicator);
        Assert.Equal(NetworkIndicatorGlyphs.For(NetworkIndicatorState.Disconnected), viewModel.NetworkIndicatorGlyph);
        Assert.Equal(viewModel.Strings["Network.State.Disconnected"], viewModel.NetworkIndicatorToolTip);
    }

    [Theory]
    [InlineData(InternetStatus.Online, true, false, NetworkIndicatorState.Ethernet, "Network.State.Ethernet")]
    [InlineData(InternetStatus.Online, false, true, NetworkIndicatorState.Wifi, "Network.State.Wifi")]
    [InlineData(InternetStatus.Online, true, true, NetworkIndicatorState.Ethernet, "Network.State.Ethernet")]
    [InlineData(InternetStatus.NoInternet, true, false, NetworkIndicatorState.Limited, "Network.State.Limited")]
    [InlineData(InternetStatus.CaptivePortal, false, true, NetworkIndicatorState.Limited, "Network.State.Limited")]
    [InlineData(InternetStatus.Checking, false, true, NetworkIndicatorState.Limited, "Network.State.Limited")]
    [InlineData(InternetStatus.NoInternet, false, false, NetworkIndicatorState.Disconnected, "Network.State.Disconnected")]
    [InlineData(InternetStatus.Online, false, false, NetworkIndicatorState.Disconnected, "Network.State.Disconnected")]
    public void NetworkIndicator_FollowsTheCoordinatorState(
        InternetStatus internet,
        bool ethernetConnected,
        bool wifiConnected,
        NetworkIndicatorState expected,
        string toolTipKey)
    {
        using MainWindowViewModel viewModel = CreateViewModel();
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        _coordinator.Publish(CreateNetworkState(internet, ethernetConnected, wifiConnected));

        Assert.Equal(expected, viewModel.NetworkIndicator);
        Assert.Equal(NetworkIndicatorGlyphs.For(expected), viewModel.NetworkIndicatorGlyph);
        Assert.Equal(viewModel.Strings[toolTipKey], viewModel.NetworkIndicatorToolTip);
        Assert.Equal(expected != NetworkIndicatorState.Disconnected, changed.Contains(nameof(MainWindowViewModel.NetworkIndicator)));
    }

    [Fact]
    public void NetworkIndicator_ReadsTheCurrentStateInsteadOfAStaleArgument()
    {
        using MainWindowViewModel viewModel = CreateViewModel();
        _coordinator.State = CreateNetworkState(InternetStatus.Online, ethernetConnected: true, wifiConnected: false);

        _coordinator.RaiseStale(CreateNetworkState(InternetStatus.NoInternet, ethernetConnected: false, wifiConnected: false));

        Assert.Equal(NetworkIndicatorState.Ethernet, viewModel.NetworkIndicator);
    }

    [Fact]
    public void NetworkIndicator_StopsFollowingTheCoordinatorOnceDisposed()
    {
        MainWindowViewModel viewModel = CreateViewModel();
        viewModel.Dispose();

        _coordinator.Publish(CreateNetworkState(InternetStatus.Online, ethernetConnected: true, wifiConnected: false));

        Assert.Equal(NetworkIndicatorState.Disconnected, viewModel.NetworkIndicator);
    }

    private static NetworkState CreateNetworkState(InternetStatus internet, bool ethernetConnected, bool wifiConnected)
    {
        EthernetState ethernet = ethernetConnected
            ? new EthernetState([new EthernetInterfaceState("eth0", "Ethernet", true, IpConfigurationKind.Dhcp, "192.168.1.20", "192.168.1.1")], Dot1xState.NotConfigured)
            : EthernetState.Empty;
        WifiState wifi = wifiConnected
            ? new WifiState(
                WifiAvailability.Available,
                new WifiConnection(Guid.NewGuid(), WifiSsid.FromText("Foundry Lab"), "Foundry Lab", WifiSecurityType.Wpa2Personal),
                [],
                ProvisionedWifiState.NotConfigured)
            : WifiState.Initial;

        return new NetworkState(ethernet, wifi, internet, ConnectionMode.Automatic, null, null, new DateTimeOffset(2026, 10, 29, 8, 0, 0, TimeSpan.Zero));
    }

    private MainWindowViewModel CreateViewModel(
        BootMediaUpdateReason bootMediaUpdateReason = BootMediaUpdateReason.None,
        IKeyboardLayoutSwitcher? keyboardLayoutSwitcher = null,
        bool? isDebugSafeMode = null)
    {
        var configuration = new FoundryConnectConfigurationDocument
        {
            Capabilities = new ConnectNetworkCapabilitiesSettings { WifiProvisioned = true },
            Wifi = new ConnectWifiSettings { IsEnabled = true, Ssid = "Foundry Lab", SecurityType = "WPA3-Personal" }
        };
        var actions = new ConnectNetworkActions(_coordinator, new FakeUnlockService(), new RecordingLogger<ConnectNetworkActions>());
        var dispatcher = new ImmediateUiDispatcher();
        var diagnostics = new DiagnosticsViewModel(_localization, new RecordingLogger<DiagnosticsViewModel>());
        var page = new ConnectPageViewModel(
            _localization, _coordinator, _monitor, actions,
            new AutoContinueViewModel(_localization, _exit, _lifetime, _clock, dispatcher, new RecordingLogger<AutoContinueViewModel>(), isAutoContinueEnabled: true),
            new EthernetSectionViewModel(_localization),
            new ProvisionedWifiViewModel(_localization, actions, configuration),
            new WifiNetworksViewModel(_localization, actions, _monitor),
            diagnostics,
            dispatcher, _clock, new RecordingLogger<ConnectPageViewModel>());

        var themeService = new FakeThemeService();
        var shell = new FakeApplicationShellService();
        var configurationService = new FakeConnectConfigurationService(configuration, bootMediaUpdateReason);
        IKeyboardLayoutSwitcher switcher = keyboardLayoutSwitcher ?? new FakeKeyboardLayoutSwitcher("0000040C");
        var logger = new RecordingLogger<MainWindowViewModel>();

        return isDebugSafeMode is bool debugSafeMode
            ? new MainWindowViewModel(themeService, _localization, shell, _lifetime, configurationService, switcher, _coordinator, page, diagnostics, logger, debugSafeMode)
            : new MainWindowViewModel(themeService, _localization, shell, _lifetime, configurationService, switcher, _coordinator, page, diagnostics, logger);
    }

    private sealed class FakeKeyboardLayoutSwitcher(string activeLayoutId) : IKeyboardLayoutSwitcher
    {
        private static readonly IReadOnlyList<KeyboardLayout> Layouts = KeyboardLayoutCatalog.FromEntries(
        [
            new KeyboardLayoutRegistryEntry("0000040C", "KBDFR.DLL", "French (Legacy, AZERTY)", null),
            new KeyboardLayoutRegistryEntry("0000080C", "KBDBE.DLL", "Belgian French", null),
            new KeyboardLayoutRegistryEntry("00000413", "KBDNE.DLL", "Dutch", null)
        ]);

        private string activeLayoutId = activeLayoutId;

        public bool FailApply { get; init; }

        public List<string> AppliedLayoutIds { get; } = [];

        public IReadOnlyList<KeyboardLayout> GetLayouts() => Layouts;

        public string GetActiveLayoutId() => activeLayoutId;

        public KeyboardLayoutSwitchResult Apply(KeyboardLayout layout)
        {
            if (FailApply)
            {
                return new KeyboardLayoutSwitchResult(false, layout.Id, "Simulated failure.");
            }

            AppliedLayoutIds.Add(layout.Id);
            activeLayoutId = layout.Id;
            return new KeyboardLayoutSwitchResult(true, layout.Id, null);
        }
    }

    private sealed class FakeThemeService : IThemeService
    {
        public ThemeMode CurrentTheme { get; private set; } = ThemeMode.System;

        public void SetTheme(ThemeMode theme) => CurrentTheme = theme;
    }

    private sealed class FakeApplicationShellService : IApplicationShellService
    {
        public bool ConfirmWarning(string title, string message) => true;

        public void Shutdown()
        {
        }

        public void ShowAbout()
        {
        }
    }

    private sealed class FakeConnectConfigurationService(
        FoundryConnectConfigurationDocument configuration,
        BootMediaUpdateReason bootMediaUpdateReason = BootMediaUpdateReason.None) : IConnectConfigurationService
    {
        public string? ConfigurationPath => null;

        public bool IsLoadedFromDisk => false;

        public bool IsBootMediaUpdateRecommended => bootMediaUpdateReason != BootMediaUpdateReason.None;
        public BootMediaUpdateReason BootMediaUpdateReason => bootMediaUpdateReason;

        public ConnectConfigurationLoadResult Load()
        {
            return ConnectConfigurationLoadResult.Success(configuration, ConnectRuntimeSecrets.None(), bootMediaUpdateReason);
        }
    }
}
