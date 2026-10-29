// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Runtime;
using Foundry.Wpf.Shell;
using Foundry.Connect.Lifetime;
using Foundry.Connect.Configuration;
using Foundry.Wpf.Theme;
using Foundry.Localization;
using Foundry.Utilities.Globalization;
using Foundry.Wpf.Localization;
using Foundry.Wpf.Runtime;
using Foundry.Wpf.ViewModels;
using Microsoft.Extensions.Logging;
using ConnectThemeMode = Foundry.Wpf.Theme.ThemeMode;

namespace Foundry.Connect.ViewModels;

/// <summary>
/// The Foundry.Connect window shell: the menu bar (theme, language, keyboard, tools, debug scenarios, about), the
/// boot-media banner and the version, around the single <see cref="ConnectPageViewModel"/>.
/// </summary>
/// <remarks>
/// The shell holds no network state: the page renders it and owns the startup sequence. The Debug menu and its
/// command exist only in debug safety mode: outside it the menu is hidden and the command refuses to run, so a
/// simulated state can never be shown in a shipped runtime. The page, its children and the diagnostics are DI
/// singletons disposed by the container, not by <see cref="Dispose"/>.
/// </remarks>
public partial class MainWindowViewModel : LocalizedViewModelBase
{
    private readonly IThemeService _themeService;
    private readonly IApplicationShellService _applicationShellService;
    private readonly IApplicationLifetimeService _applicationLifetimeService;
    private readonly IConnectConfigurationService _configurationService;
    private readonly IKeyboardLayoutSwitcher _keyboardLayoutSwitcher;
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly CancellationTokenSource _disposeCts = new();
    private bool _isInitialized;
    private bool _isDisposed;

    /// <summary>
    /// Initializes the shell; the Debug menu follows <see cref="DebugSafetyMode.IsEnabled"/>.
    /// </summary>
    /// <param name="themeService">Applies the theme menu choices.</param>
    /// <param name="localizationService">Source of the texts and of the language menu.</param>
    /// <param name="applicationShellService">Shows the About dialog.</param>
    /// <param name="applicationLifetimeService">Receives the user-aborted exit when the window closes.</param>
    /// <param name="configurationService">Tells whether a boot-media update is recommended.</param>
    /// <param name="keyboardLayoutSwitcher">Lists and applies the keyboard layouts.</param>
    /// <param name="page">The single Connect page.</param>
    /// <param name="diagnostics">The Tools menu diagnostics.</param>
    /// <param name="logger">Logger for keyboard layout changes and the window close.</param>
    public MainWindowViewModel(
        IThemeService themeService,
        ILocalizationService localizationService,
        IApplicationShellService applicationShellService,
        IApplicationLifetimeService applicationLifetimeService,
        IConnectConfigurationService configurationService,
        IKeyboardLayoutSwitcher keyboardLayoutSwitcher,
        ConnectPageViewModel page,
        DiagnosticsViewModel diagnostics,
        ILogger<MainWindowViewModel> logger)
        : this(
            themeService,
            localizationService,
            applicationShellService,
            applicationLifetimeService,
            configurationService,
            keyboardLayoutSwitcher,
            page,
            diagnostics,
            logger,
            DebugSafetyMode.IsEnabled)
    {
    }

    /// <summary>
    /// Initializes the shell with an explicit debug safety mode, for tests.
    /// </summary>
    internal MainWindowViewModel(
        IThemeService themeService,
        ILocalizationService localizationService,
        IApplicationShellService applicationShellService,
        IApplicationLifetimeService applicationLifetimeService,
        IConnectConfigurationService configurationService,
        IKeyboardLayoutSwitcher keyboardLayoutSwitcher,
        ConnectPageViewModel page,
        DiagnosticsViewModel diagnostics,
        ILogger<MainWindowViewModel> logger,
        bool isDebugSafeMode)
        : base(localizationService)
    {
        _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
        _applicationShellService = applicationShellService ?? throw new ArgumentNullException(nameof(applicationShellService));
        _applicationLifetimeService = applicationLifetimeService ?? throw new ArgumentNullException(nameof(applicationLifetimeService));
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _keyboardLayoutSwitcher = keyboardLayoutSwitcher ?? throw new ArgumentNullException(nameof(keyboardLayoutSwitcher));
        Page = page ?? throw new ArgumentNullException(nameof(page));
        Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        IsDebugMenuVisible = isDebugSafeMode;

        LocalizationService.LanguageChanged += OnLanguageChanged;
        RefreshSupportedCultures();
        RefreshKeyboardMenu();
        RefreshDebugScenarios();
    }

    /// <summary>Gets the single Connect page.</summary>
    public ConnectPageViewModel Page { get; }

    /// <summary>Gets the Tools menu diagnostics: open the log file and export a support bundle.</summary>
    public DiagnosticsViewModel Diagnostics { get; }

    /// <summary>Gets the supported UI cultures.</summary>
    public ObservableCollection<SupportedCultureOption> SupportedCultures { get; } = [];

    /// <summary>Gets the keyboard menu entries: the active language's layouts, then the other languages.</summary>
    public ObservableCollection<KeyboardMenuEntry> KeyboardMenuEntries { get; } = [];

    /// <summary>
    /// Gets the Debug › Scenarios entries, <see cref="ConnectDebugScenario.Live"/> first, with the active one checked.
    /// Empty outside debug safety mode.
    /// </summary>
    public ObservableCollection<DebugScenarioOption> DebugScenarios { get; } = [];

    /// <summary>Gets the keyboard menu header, including the active layout's short name when it is known.</summary>
    public string KeyboardMenuHeader { get; private set; } = string.Empty;

    /// <summary>Gets the current application theme mode.</summary>
    public ConnectThemeMode CurrentTheme => _themeService.CurrentTheme;

    /// <summary>Gets the current UI culture.</summary>
    public CultureInfo CurrentCulture => LocalizationService.CurrentCulture;

    /// <summary>Gets the localized window title.</summary>
    public string WindowTitle => LocalizationService.GetString("App.WindowTitle");

    /// <summary>Gets the version shown at the right of the menu bar.</summary>
    public string VersionDisplay => LocalizationService.Format("Common.VersionFormat", FoundryConnectApplicationInfo.Version);

    /// <summary>Gets whether the boot-media banner is shown.</summary>
    public bool IsBootMediaUpdateRecommended => _configurationService.IsBootMediaUpdateRecommended;

    /// <summary>Gets the boot-media banner caption for the update reason.</summary>
    public string BootMediaUpdateRecommendedText => LocalizationService.GetString(
        _configurationService.BootMediaUpdateReason == BootMediaUpdateReason.NewerRelease
            ? "BootMedia.UpdateAvailable"
            : "BootMedia.UpdateRecommended");

    /// <summary>Gets the boot-media banner tooltip for the update reason.</summary>
    public string BootMediaUpdateRecommendedToolTip => LocalizationService.GetString(
        _configurationService.BootMediaUpdateReason == BootMediaUpdateReason.UnknownAuthoringVersion
            ? "BootMedia.UnknownAuthoringVersionToolTip"
            : "BootMedia.UpdateRecommendedToolTip");

    /// <summary>
    /// Gets whether the Debug menu is shown: only in debug safety mode, a Debug build with a debugger attached.
    /// </summary>
    public bool IsDebugMenuVisible { get; }

    /// <summary>
    /// Initializes the page once: provisioning, then the network monitor. Later calls do nothing.
    /// </summary>
    /// <returns>A task that completes once the page is initialized, or when the shell is disposed first.</returns>
    public async Task InitializeAsync()
    {
        if (_isDisposed || _isInitialized)
        {
            return;
        }

        _isInitialized = true;

        // Read before the first await: Dispose disposes the source.
        CancellationToken lifetime = _disposeCts.Token;
        await Page.InitializeAsync(lifetime);
    }

    /// <summary>
    /// Handles a user closing the window before Foundry.Connect has completed successfully.
    /// </summary>
    public void HandleWindowClosing()
    {
        // TryExit decides atomically, so a controlled exit that won the race keeps its exit code.
        if (_applicationLifetimeService.TryExit(ChildRuntimeExitCode.UserAborted))
        {
            _logger.LogInformation("Window close detected before a controlled exit. Returning user-aborted exit code.");
        }
    }

    /// <summary>
    /// Unsubscribes from language changes and cancels a pending initialization. Idempotent.
    /// </summary>
    public override void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        LocalizationService.LanguageChanged -= OnLanguageChanged;
        _disposeCts.Cancel();
        _disposeCts.Dispose();
        base.Dispose();
    }

    [RelayCommand]
    private void SetSystemTheme() => SetTheme(ConnectThemeMode.System);

    [RelayCommand]
    private void SetLightTheme() => SetTheme(ConnectThemeMode.Light);

    [RelayCommand]
    private void SetDarkTheme() => SetTheme(ConnectThemeMode.Dark);

    [RelayCommand]
    private void SetCulture(string cultureName)
    {
        LocalizationService.SetCulture(new CultureInfo(cultureName));
    }

    [RelayCommand]
    private void SetKeyboardLayout(string? layoutId)
    {
        KeyboardLayout? layout = KeyboardLayoutSelection.Find(_keyboardLayoutSwitcher.GetLayouts(), layoutId);
        if (layout is null)
        {
            return;
        }

        KeyboardLayoutSwitchResult result = _keyboardLayoutSwitcher.Apply(layout);
        if (!result.Succeeded)
        {
            _logger.LogWarning("Keyboard layout change failed. LayoutId={LayoutId}, Error={Error}", layout.Id, result.Error);
        }
        else if (result.Error is not null)
        {
            _logger.LogWarning("Keyboard layout changed with a warning. LayoutId={LayoutId}, Warning={Warning}", layout.Id, result.Error);
        }
        else
        {
            _logger.LogInformation("Keyboard layout changed. LayoutId={LayoutId}", layout.Id);
        }

        RefreshKeyboardMenu();
    }

    [RelayCommand]
    private void ShowAbout()
    {
        _applicationShellService.ShowAbout();
    }

    // Guarded twice: CanExecute keeps the menu entries disabled, and the body refuses to run outside debug safety mode.
    [RelayCommand(CanExecute = nameof(IsDebugMenuVisible))]
    private void SetDebugScenario(ConnectDebugScenario scenario)
    {
        if (!IsDebugMenuVisible)
        {
            return;
        }

        Page.ApplyDebugScenario(scenario);
        RefreshDebugScenarios();
    }

    private void SetTheme(ConnectThemeMode theme)
    {
        _themeService.SetTheme(theme);
        OnPropertyChanged(nameof(CurrentTheme));
    }

    private void RefreshKeyboardMenu()
    {
        IReadOnlyList<KeyboardLayout> layouts = _keyboardLayoutSwitcher.GetLayouts();
        string activeLayoutId = _keyboardLayoutSwitcher.GetActiveLayoutId();
        KeyboardMenuEntries.Clear();
        foreach (KeyboardMenuEntry entry in KeyboardLayoutSelection.BuildMenuEntries(layouts, activeLayoutId, LocalizationService.GetString("Keyboard.OtherLanguages")))
        {
            KeyboardMenuEntries.Add(entry);
        }

        KeyboardLayout? activeLayout = KeyboardLayoutSelection.Find(layouts, activeLayoutId);
        KeyboardMenuHeader = activeLayout is null
            ? LocalizationService.GetString("Menu.Keyboard")
            : LocalizationService.Format("Menu.KeyboardFormat", activeLayout.ShortName);
        OnPropertyChanged(nameof(KeyboardMenuHeader));
    }

    private void RefreshSupportedCultures()
    {
        SupportedCultures.Clear();
        foreach (SupportedCultureOption option in LocalizationService.CreateSupportedCultureOptions())
        {
            SupportedCultures.Add(option);
        }
    }

    private void RefreshDebugScenarios()
    {
        DebugScenarios.Clear();
        if (!IsDebugMenuVisible)
        {
            return;
        }

        foreach (ConnectDebugScenario scenario in DebugScenarioCatalog.All)
        {
            DebugScenarios.Add(new DebugScenarioOption(
                scenario,
                LocalizationService.GetString(DebugScenarioCatalog.ResourceKey(scenario)),
                scenario == Page.ActiveDebugScenario));
        }
    }

    private void OnLanguageChanged(object? sender, ApplicationLanguageChangedEventArgs e)
    {
        RunOnUiThread(() =>
        {
            if (_isDisposed)
            {
                return;
            }

            RefreshSupportedCultures();
            RefreshKeyboardMenu();
            RefreshDebugScenarios();
            OnPropertyChanged(nameof(CurrentCulture));
            OnPropertyChanged(nameof(WindowTitle));
            OnPropertyChanged(nameof(VersionDisplay));
            OnPropertyChanged(nameof(BootMediaUpdateRecommendedText));
            OnPropertyChanged(nameof(BootMediaUpdateRecommendedToolTip));
        });
    }
}

/// <summary>
/// One Debug › Scenarios menu entry.
/// </summary>
/// <param name="Scenario">The scenario applied when the entry is clicked.</param>
/// <param name="Header">The localized entry text.</param>
/// <param name="IsSelected">Whether the scenario is the one shown now.</param>
public sealed record DebugScenarioOption(ConnectDebugScenario Scenario, string Header, bool IsSelected);
