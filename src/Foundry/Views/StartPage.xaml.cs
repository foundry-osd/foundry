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
        UsbTargetCard.Header = localizationService.GetString("StartMedia.UsbTarget.Header");
        RefreshUsbButton.Content = localizationService.GetString("Common.Refresh");

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
