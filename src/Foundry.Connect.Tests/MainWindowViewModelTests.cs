// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using Foundry.Core.Models.Configuration;
using Foundry.Connect.Tests.Support;
using Foundry.Core.Models.Runtime;
using Foundry.Wpf.Shell;
using Foundry.Connect.Configuration;
using Foundry.Wpf.Theme;
using Foundry.Connect.ViewModels;
using Foundry.Utilities.Globalization;
using Foundry.Wpf.Localization;

namespace Foundry.Connect.Tests;

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
        Assert.Equal(15, viewModel.DebugScenarios.Count);
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
    public async Task InitializeAsync_InitializesThePageOnce()
    {
        using MainWindowViewModel viewModel = CreateViewModel();

        await viewModel.InitializeAsync();
        await viewModel.InitializeAsync();

        Assert.Equal(1, _coordinator.ApplyProvisioningCalls);
    }

    [Fact]
    public void HandleWindowClosing_RequestsUserAbortedUnlessAnExitWon()
    {
        using MainWindowViewModel viewModel = CreateViewModel();

        viewModel.HandleWindowClosing();
        viewModel.HandleWindowClosing();

        Assert.Equal([ChildRuntimeExitCode.UserAborted], _lifetime.Codes);
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
        var actions = new ConnectNetworkActions(_coordinator, new RecordingLogger<ConnectNetworkActions>());
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
            ? new MainWindowViewModel(themeService, _localization, shell, _lifetime, configurationService, switcher, page, diagnostics, logger, debugSafeMode)
            : new MainWindowViewModel(themeService, _localization, shell, _lifetime, configurationService, switcher, page, diagnostics, logger);
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

        public KeyboardLayoutSwitchResult? RestoreHandoff() => null;
    }

    private sealed class FakeThemeService : IThemeService
    {
        public ThemeMode CurrentTheme => ThemeMode.System;

        public void SetTheme(ThemeMode theme)
        {
        }
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
