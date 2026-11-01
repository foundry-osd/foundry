// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Foundry.AutopilotInteractive.Services.Device;
using Foundry.Core.Models.Autopilot;
using Foundry.Localization;
using Foundry.Wpf.Localization;
using Foundry.Wpf.Runtime;
using Foundry.Wpf.Shell;
using Foundry.Wpf.Theme;
using Foundry.Wpf.ViewModels;

namespace Foundry.AutopilotInteractive.ViewModels;

/// <summary>
/// The window shell: the menu bar (theme, language, debug, about), the version and the device footer shown on every page.
/// </summary>
/// <remarks>
/// The shell holds no registration state. The Debug menu is shown only in debug safety mode, so a simulated state can
/// never be reached in a shipped runtime.
/// </remarks>
public sealed partial class MainWindowViewModel : LocalizedViewModelBase
{
    private readonly IThemeService _themeService;
    private readonly IApplicationShellService _applicationShellService;
    private readonly IDeviceInformationReader _deviceInformationReader;
    private readonly string? _assignedComputerName;
    private DeviceInformation _deviceInformation = new(null, null, null);
    private bool _isInitialized;
    private bool _hasDeviceInformation;
    private bool _isDisposed;

    /// <summary>
    /// Initializes the shell; the footer stays empty until <see cref="InitializeAsync"/> has read the device.
    /// </summary>
    /// <param name="localizationService">Source of the texts and of the language menu.</param>
    /// <param name="themeService">Applies the theme menu choices.</param>
    /// <param name="applicationShellService">Shows the About dialog.</param>
    /// <param name="deviceInformationReader">Reads the hardware identity for the footer.</param>
    /// <param name="configuration">Runtime configuration; its computer name is appended to the footer.</param>
    public MainWindowViewModel(
        ILocalizationService localizationService,
        IThemeService themeService,
        IApplicationShellService applicationShellService,
        IDeviceInformationReader deviceInformationReader,
        AutopilotInteractiveRuntimeConfiguration configuration)
        : base(localizationService)
    {
        ArgumentNullException.ThrowIfNull(themeService);
        ArgumentNullException.ThrowIfNull(applicationShellService);
        ArgumentNullException.ThrowIfNull(deviceInformationReader);
        ArgumentNullException.ThrowIfNull(configuration);

        _themeService = themeService;
        _applicationShellService = applicationShellService;
        _deviceInformationReader = deviceInformationReader;
        _assignedComputerName = configuration.AssignedComputerName;
        IsDebugMenuVisible = DebugSafetyMode.IsEnabled;

        LocalizationService.LanguageChanged += OnLanguageChanged;
        RefreshSupportedCultures();
    }

    /// <summary>Gets the supported UI cultures, the active one marked as selected.</summary>
    public ObservableCollection<SupportedCultureOption> SupportedCultures { get; } = [];

    /// <summary>Gets the current application theme mode.</summary>
    public ThemeMode CurrentTheme => _themeService.CurrentTheme;

    /// <summary>Gets the localized window title.</summary>
    public string WindowTitle => LocalizationService.GetString("App.Name");

    /// <summary>Gets the version shown at the right of the menu bar.</summary>
    public string VersionDisplay => LocalizationService.Format("Common.VersionFormat", FoundryAutopilotInteractiveApplicationInfo.Version);

    /// <summary>Gets whether the Debug menu is shown: a Debug build with a debugger attached.</summary>
    public bool IsDebugMenuVisible { get; }

    /// <summary>Gets the footer line describing the device, empty until <see cref="InitializeAsync"/> completes.</summary>
    public string DeviceFooter { get; private set; } = string.Empty;

    /// <summary>
    /// Reads the device identity off the UI thread and fills the footer. Later calls do nothing.
    /// </summary>
    /// <returns>A task that completes once the footer is set.</returns>
    public async Task InitializeAsync()
    {
        if (_isDisposed || _isInitialized)
        {
            return;
        }

        _isInitialized = true;
        DeviceInformation information = await Task.Run(_deviceInformationReader.Read);
        if (_isDisposed)
        {
            return;
        }

        _deviceInformation = information;
        _hasDeviceInformation = true;
        RefreshDeviceFooter();
    }

    /// <summary>Unsubscribes from language changes. Idempotent.</summary>
    public override void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        LocalizationService.LanguageChanged -= OnLanguageChanged;
        base.Dispose();
    }

    [RelayCommand]
    private void SetSystemTheme() => SetTheme(ThemeMode.System);

    [RelayCommand]
    private void SetLightTheme() => SetTheme(ThemeMode.Light);

    [RelayCommand]
    private void SetDarkTheme() => SetTheme(ThemeMode.Dark);

    [RelayCommand]
    private void SetCulture(string cultureName)
    {
        LocalizationService.SetCulture(new CultureInfo(cultureName));
    }

    [RelayCommand]
    private void ShowAbout()
    {
        _applicationShellService.ShowAbout();
    }

    private void SetTheme(ThemeMode theme)
    {
        _themeService.SetTheme(theme);
        OnPropertyChanged(nameof(CurrentTheme));
    }

    private void RefreshSupportedCultures()
    {
        SupportedCultures.Clear();
        foreach (SupportedCultureOption option in LocalizationService.CreateSupportedCultureOptions())
        {
            SupportedCultures.Add(option);
        }
    }

    // The unknown-value text is localized, so the footer is rebuilt from the cached identity instead of re-reading WMI.
    private void RefreshDeviceFooter()
    {
        DeviceFooter = DeviceFooterFormatter.Format(
            _deviceInformation,
            _assignedComputerName,
            LocalizationService.GetString("Footer.Unknown"));
        OnPropertyChanged(nameof(DeviceFooter));
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
            OnPropertyChanged(nameof(WindowTitle));
            OnPropertyChanged(nameof(VersionDisplay));
            if (_hasDeviceInformation)
            {
                RefreshDeviceFooter();
            }
        });
    }
}
