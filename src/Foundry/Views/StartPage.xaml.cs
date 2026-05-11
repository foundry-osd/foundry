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
        ReadinessPrerequisitesCard.Header = localizationService.GetString("StartMedia.Readiness.Prerequisites.Header");
        ReadinessPrerequisitesCard.Description = localizationService.GetString("StartMedia.Readiness.Prerequisites.Description");
        ReadinessMediaOutputCard.Header = localizationService.GetString("StartMedia.Readiness.MediaOutput.Header");
        ReadinessMediaOutputCard.Description = localizationService.GetString("StartMedia.Readiness.MediaOutput.Description");
        ReadinessExpertConfigurationCard.Header = localizationService.GetString("StartMedia.Readiness.ExpertConfiguration.Header");
        ReadinessExpertConfigurationCard.Description = localizationService.GetString("StartMedia.Readiness.ExpertConfiguration.Description");

        IsoPathCard.Header = localizationService.GetString("StartMedia.IsoPath.Header");
        IsoPathCard.Description = localizationService.GetString("StartMedia.IsoPath.Description");
        BrowseIsoButton.Content = localizationService.GetString("Common.Browse");

        UsbTargetCard.Header = localizationService.GetString("StartMedia.UsbTarget.Header");
        RefreshUsbButton.Content = localizationService.GetString("Common.Refresh");
        UsbPartitionStyleCard.Header = localizationService.GetString("StartMedia.Field.PartitionStyle");
        UsbPartitionStyleCard.Description = localizationService.GetString("StartMedia.UsbLayout.PartitionStyle.Description");
        UsbFormatModeCard.Header = localizationService.GetString("StartMedia.Field.FormatMode");
        UsbFormatModeCard.Description = localizationService.GetString("StartMedia.UsbLayout.FormatMode.Description");

        FinalCommandsCard.Header = localizationService.GetString("StartMedia.FinalCommands.Header");
        FinalCommandsCard.Description = localizationService.GetString("StartMedia.FinalCommands.Description");
        CreateIsoButton.Content = localizationService.GetString("StartMedia.CreateIsoButton");
        CreateUsbButton.Content = localizationService.GetString("StartMedia.CreateUsbButton");
    }

    private void ReadinessActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: StartReadinessNavigationTarget navigationTarget })
        {
            return;
        }

        switch (navigationTarget)
        {
            case StartReadinessNavigationTarget.Adk:
                App.Current.NavService.NavigateTo(typeof(AdkPage), localizationService.GetString("Adk.PageTitle"));
                break;
            case StartReadinessNavigationTarget.General:
                App.Current.NavService.NavigateTo(typeof(GeneralConfigurationPage), localizationService.GetString("GeneralConfigurationPage_Title.Text"));
                break;
            case StartReadinessNavigationTarget.Network:
                App.Current.NavService.NavigateTo(typeof(NetworkPage), localizationService.GetString("NetworkPage_Title.Text"));
                break;
            case StartReadinessNavigationTarget.Autopilot:
                App.Current.NavService.NavigateTo(typeof(AutopilotPage), localizationService.GetString("AutopilotPage_Title.Text"));
                break;
            case StartReadinessNavigationTarget.Customization:
                App.Current.NavService.NavigateTo(typeof(CustomizationPage), localizationService.GetString("CustomizationPage_Title.Text"));
                break;
        }
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
