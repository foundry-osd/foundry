using Foundry.Services.Localization;
using Serilog;

namespace Foundry.Views;

public sealed partial class GeneralConfigurationPage : Page
{
    private bool isInitializingWinPeLanguageSelection = true;
    private readonly IApplicationLocalizationService localizationService;
    private readonly ILogger logger = Log.ForContext<GeneralConfigurationPage>();

    public GeneralConfigurationViewModel ViewModel { get; }

    public GeneralConfigurationPage()
    {
        localizationService = App.GetService<IApplicationLocalizationService>();
        ViewModel = App.GetService<GeneralConfigurationViewModel>();
        InitializeComponent();
        ApplyLocalizedText();
        localizationService.LanguageChanged += OnLanguageChanged;
        Unloaded += OnUnloaded;
        isInitializingWinPeLanguageSelection = false;
    }

    private void WinPeLanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isInitializingWinPeLanguageSelection)
        {
            return;
        }

        if (e.AddedItems.FirstOrDefault() is string selectedLanguage)
        {
            ViewModel.SetWinPeLanguage(selectedLanguage);
        }
    }

    private void CreateMediaButton_Click(object sender, RoutedEventArgs e)
    {
        App.Current.NavService.NavigateTo(typeof(StartPage), localizationService.GetString("StartPage_Title.Text"));
    }

    private void OnLanguageChanged(object? sender, ApplicationLanguageChangedEventArgs e)
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            ApplyLocalizedText();
            return;
        }

        if (!DispatcherQueue.TryEnqueue(ApplyLocalizedText))
        {
            logger.Warning(
                "Failed to enqueue general configuration localization refresh. OldLanguage={OldLanguage}, NewLanguage={NewLanguage}",
                e.OldLanguage,
                e.NewLanguage);
        }
    }

    private void ApplyLocalizedText()
    {
        IsoPathCard.Header = localizationService.GetString("StartMedia.IsoPath.Header");
        IsoPathCard.Description = localizationService.GetString("StartMedia.IsoPath.Description");
        BrowseIsoButton.Content = localizationService.GetString("Common.Browse");

        ArchitectureCard.Header = localizationService.GetString("StartMedia.Architecture.Header");
        ArchitectureCard.Description = localizationService.GetString("StartMedia.Architecture.Description");
        Ca2023Toggle.OnContent = localizationService.GetString("StartMedia.Signature.Ca2023");
        Ca2023Toggle.OffContent = localizationService.GetString("StartMedia.Signature.Ca2011");

        WinPeLanguageCard.Header = localizationService.GetString("StartMedia.WinPeLanguage.Header");
        WinPeLanguageRefreshButton.Content = localizationService.GetString("Common.Refresh");

        UsbLayoutCard.Header = localizationService.GetString("StartMedia.UsbLayout.Header");
        UsbLayoutCard.Description = localizationService.GetString("StartMedia.UsbLayout.Description");

        DriverOptionsCard.Header = localizationService.GetString("StartMedia.DriverOptions.Header");
        DriverOptionsCard.Description = localizationService.GetString("StartMedia.DriverOptions.Description");
        DellDriversToggle.OnContent = localizationService.GetString("StartMedia.DriverOptions.Dell");
        DellDriversToggle.OffContent = localizationService.GetString("StartMedia.DriverOptions.Dell");
        HpDriversToggle.OnContent = localizationService.GetString("StartMedia.DriverOptions.Hp");
        HpDriversToggle.OffContent = localizationService.GetString("StartMedia.DriverOptions.Hp");
        CustomDriverDirectoryCard.Header = localizationService.GetString("StartMedia.CustomDrivers.Header");
        CustomDriverDirectoryCard.Description = localizationService.GetString("StartMedia.CustomDrivers.Description");
        BrowseCustomDriversButton.Content = localizationService.GetString("Common.Browse");

        CreateMediaCard.Header = localizationService.GetString("GeneralConfiguration_CreateMedia.Header");
        CreateMediaCard.Description = localizationService.GetString("GeneralConfiguration_CreateMedia.Description");
        CreateMediaButton.Content = localizationService.GetString("GeneralConfiguration_CreateMedia.Button");

        ViewModel.RefreshLocalizedOptions();

        bool wasInitializingWinPeLanguageSelection = isInitializingWinPeLanguageSelection;
        isInitializingWinPeLanguageSelection = true;
        ViewModel.RefreshWinPeLanguages();
        WinPeLanguageCard.Description = string.IsNullOrWhiteSpace(ViewModel.WinPeLanguageStatus)
            ? localizationService.GetString("StartMedia.WinPeLanguage.Description")
            : ViewModel.WinPeLanguageStatus;
        WinPeLanguageComboBox.SelectedItem = ViewModel.SelectedWinPeLanguage;
        isInitializingWinPeLanguageSelection = wasInitializingWinPeLanguageSelection;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        localizationService.LanguageChanged -= OnLanguageChanged;
        Unloaded -= OnUnloaded;
    }
}
