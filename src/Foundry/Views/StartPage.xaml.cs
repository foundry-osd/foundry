using Foundry.Services.Localization;

namespace Foundry.Views;

public sealed partial class StartPage : Page
{
    private readonly IApplicationLocalizationService localizationService;

    public StartMediaViewModel ViewModel { get; }

    public StartPage()
    {
        localizationService = App.GetService<IApplicationLocalizationService>();
        ViewModel = App.GetService<StartMediaViewModel>();
        InitializeComponent();
        ApplyLocalizedText();
        localizationService.LanguageChanged += OnLanguageChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.InitializeAsync();
    }

    private void ApplyLocalizedText()
    {
        IsoSummaryButton.Content = localizationService.GetString("StartMedia.IsoSummaryButton");
        UsbSummaryButton.Content = localizationService.GetString("StartMedia.UsbSummaryButton");

        IsoPathCard.Header = localizationService.GetString("StartMedia.IsoPath.Header");
        IsoPathCard.Description = localizationService.GetString("StartMedia.IsoPath.Description");
        BrowseIsoButton.Content = localizationService.GetString("Common.Browse");

        ArchitectureCard.Header = localizationService.GetString("StartMedia.Architecture.Header");
        ArchitectureCard.Description = localizationService.GetString("StartMedia.Architecture.Description");
        Ca2023Toggle.OnContent = localizationService.GetString("StartMedia.Signature.Ca2023");
        Ca2023Toggle.OffContent = localizationService.GetString("StartMedia.Signature.Ca2011");

        WinPeLanguageCard.Header = localizationService.GetString("StartMedia.WinPeLanguage.Header");
        WinPeLanguageCard.Description = localizationService.GetString("StartMedia.WinPeLanguage.Description");

        UsbTargetCard.Header = localizationService.GetString("StartMedia.UsbTarget.Header");
        RefreshUsbButton.Content = localizationService.GetString("Common.Refresh");

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

        FinalCommandsCard.Header = localizationService.GetString("StartMedia.FinalCommands.Header");
        FinalCommandsCard.Description = localizationService.GetString("StartMedia.FinalCommands.Description");
        CreateIsoButton.Content = localizationService.GetString("StartMedia.CreateIsoButton");
        CreateUsbButton.Content = localizationService.GetString("StartMedia.CreateUsbButton");
    }

    private void OnLanguageChanged(object? sender, ApplicationLanguageChangedEventArgs e)
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            ApplyLocalizedText();
            return;
        }

        _ = DispatcherQueue.TryEnqueue(ApplyLocalizedText);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Dispose();
        localizationService.LanguageChanged -= OnLanguageChanged;
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
    }
}
