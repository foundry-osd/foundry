// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Localization;
using Foundry.Services.Localization;
using Foundry.Services.Startup;
using Serilog;

namespace Foundry.Views;

public sealed partial class GeneralSettingPage : Page
{
    private bool isInitializingLanguageSelection = true;
    private bool isInitializingStartupToggle = true;
    private readonly IApplicationLocalizationService localizationService;
    private readonly IWindowsStartupService startupService;
    private readonly ILogger logger = Log.ForContext<GeneralSettingPage>();

    public GeneralSettingViewModel ViewModel { get; }

    public GeneralSettingPage()
    {
        localizationService = App.GetService<IApplicationLocalizationService>();
        startupService = App.GetService<IWindowsStartupService>();
        ViewModel = App.GetService<GeneralSettingViewModel>();
        InitializeComponent();
        ApplyLocalizedText();
        localizationService.LanguageChanged += OnLanguageChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        isInitializingLanguageSelection = false;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        isInitializingStartupToggle = true;
        try
        {
            bool isEnabled = await startupService.IsEnabledAsync();
            if (IsLoaded)
            {
                StartupToggle.IsOn = isEnabled;
            }
        }
        finally
        {
            isInitializingStartupToggle = false;
        }
    }

    private async void StartupToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (isInitializingStartupToggle)
        {
            return;
        }

        StartupToggle.IsEnabled = false;
        try
        {
            bool isEnabled = await startupService.SetEnabledAsync(StartupToggle.IsOn);
            isInitializingStartupToggle = true;
            StartupToggle.IsOn = isEnabled;
        }
        catch (Exception ex)
        {
            logger.Warning(ex, "Failed to update Windows startup state.");
        }
        finally
        {
            isInitializingStartupToggle = false;
            StartupToggle.IsEnabled = true;
        }
    }

    private async void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isInitializingLanguageSelection)
        {
            return;
        }

        if (e.AddedItems.FirstOrDefault() is SupportedCultureOption selectedLanguage)
        {
            await ViewModel.SetLanguageAsync(selectedLanguage);
        }
    }

    private void OnLanguageChanged(object? sender, ApplicationLanguageChangedEventArgs e)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            if (!DispatcherQueue.TryEnqueue(ApplyLocalizedText))
            {
                logger.Warning(
                    "Failed to enqueue general settings localization refresh. OldLanguage={OldLanguage}, NewLanguage={NewLanguage}",
                    e.OldLanguage,
                    e.NewLanguage);
            }

            return;
        }

        ApplyLocalizedText();
    }

    private void ApplyLocalizedText()
    {
        StartupCard.Header = localizationService.GetString("GeneralSetting_StartupCard.Header");
        StartupCard.Description = localizationService.GetString("GeneralSetting_StartupCard.Description");
        LanguageCard.Header = localizationService.GetString("GeneralSetting_LanguageCard.Header");
        LanguageCard.Description = localizationService.GetString("GeneralSetting_LanguageCard.Description");
        DiagnosticsCard.Header = localizationService.GetString("GeneralSetting_DiagnosticsCard.Header");
        DiagnosticsCard.Description = localizationService.GetString("GeneralSetting_DiagnosticsCard.Description");
        ExportDiagnosticsCard.Header = localizationService.GetString("Diagnostics.ExportCardHeader");
        ExportDiagnosticsCard.Description = localizationService.GetString("Diagnostics.ExportCardDescription");
        ExportDiagnosticsButton.Content = localizationService.GetString("Diagnostics.ExportButton");
        ExportRawDiagnosticsButton.Content = localizationService.GetString("Diagnostics.ExportRawButton");

        bool wasInitializingLanguageSelection = isInitializingLanguageSelection;
        isInitializingLanguageSelection = true;
        ViewModel.RefreshSupportedLanguages();
        LanguageComboBox.SelectedItem = ViewModel.SelectedLanguage;
        isInitializingLanguageSelection = wasInitializingLanguageSelection;

    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        localizationService.LanguageChanged -= OnLanguageChanged;
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
    }
}
