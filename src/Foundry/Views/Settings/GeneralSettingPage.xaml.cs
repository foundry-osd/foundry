using Foundry.Core.Localization;
using Foundry.Services.Localization;
using Serilog;

namespace Foundry.Views;

public sealed partial class GeneralSettingPage : Page
{
    private bool isInitializingLanguageSelection = true;
    private readonly IApplicationLocalizationService localizationService;
    private readonly ILogger logger = Log.ForContext<GeneralSettingPage>();

    public GeneralSettingViewModel ViewModel { get; }

    public GeneralSettingPage()
    {
        localizationService = App.GetService<IApplicationLocalizationService>();
        ViewModel = App.GetService<GeneralSettingViewModel>();
        InitializeComponent();
        ApplyLocalizedText();
        localizationService.LanguageChanged += OnLanguageChanged;
        Unloaded += OnUnloaded;
        isInitializingLanguageSelection = false;
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
        StartupToggle.OnContent = localizationService.GetString("Common.Enabled");
        StartupToggle.OffContent = localizationService.GetString("Common.Disabled");
        DiagnosticsToggle.OnContent = localizationService.GetString("Common.Enabled");
        DiagnosticsToggle.OffContent = localizationService.GetString("Common.Disabled");

        BreadcrumbNavigator.SetPageTitle(this, localizationService.GetString("SettingsPage_GeneralCard.Header"));

        bool wasInitializingLanguageSelection = isInitializingLanguageSelection;
        isInitializingLanguageSelection = true;
        ViewModel.RefreshSupportedLanguages();
        LanguageComboBox.SelectedItem = ViewModel.SelectedLanguage;
        isInitializingLanguageSelection = wasInitializingLanguageSelection;

    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        localizationService.LanguageChanged -= OnLanguageChanged;
        Unloaded -= OnUnloaded;
    }
}
