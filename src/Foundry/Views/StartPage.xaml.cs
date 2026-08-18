// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using Foundry.Core.Models.Configuration;
using Foundry.Services.Configuration;
using Foundry.Services.Localization;

namespace Foundry.Views;

public sealed partial class StartPage : Page
{
    private readonly IApplicationLocalizationService localizationService;
    private readonly IFoundryConfigurationStateService configurationStateService;

    public StartMediaViewModel ViewModel { get; }

    public StartPage()
    {
        localizationService = App.GetService<IApplicationLocalizationService>();
        configurationStateService = App.GetService<IFoundryConfigurationStateService>();
        ViewModel = App.GetService<StartMediaViewModel>();
        InitializeComponent();
        ApplyLocalizedText();
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
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
        ReadinessFoundryConfigurationCard.Header = localizationService.GetString("StartMedia.Readiness.FoundryConfiguration.Header");
        ReadinessFoundryConfigurationCard.Description = localizationService.GetString("StartMedia.Readiness.FoundryConfiguration.Description");

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
        ApplyUsbActionButtonState();
    }

    private void ApplyUsbActionButtonState()
    {
        CreateUsbButton.Content = localizationService.GetString(ViewModel.IsSelectedUsbFoundryMedia
            ? "StartMedia.UpdateUsbButton"
            : "StartMedia.CreateUsbButton");
        if (ViewModel.IsSelectedUsbFoundryMedia)
        {
            CreateUsbButton.Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["AccentButtonStyle"];
            return;
        }

        CreateUsbButton.ClearValue(Control.StyleProperty);
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
                App.Current.NavigationService.NavigateTo(typeof(AdkPage));
                break;
            case StartReadinessNavigationTarget.General:
                App.Current.NavigationService.NavigateTo(typeof(GeneralConfigurationPage));
                break;
            case StartReadinessNavigationTarget.Network:
                App.Current.NavigationService.NavigateTo(GetNetworkPageType());
                break;
            case StartReadinessNavigationTarget.Autopilot:
                App.Current.NavigationService.NavigateTo(GetAutopilotPageType());
                break;
            case StartReadinessNavigationTarget.Customization:
                App.Current.NavigationService.NavigateTo(GetCustomizationPageType());
                break;
        }
    }

    private Type GetNetworkPageType()
    {
        NetworkSettings settings = configurationStateService.Current.Network;
        return !settings.Dot1x.IsEnabled && settings.WifiProvisioned
            ? typeof(WifiPage)
            : typeof(EthernetDot1xPage);
    }

    private Type GetAutopilotPageType()
    {
        return configurationStateService.Current.Autopilot.ProvisioningMode switch
        {
            AutopilotProvisioningMode.HardwareHashUpload => typeof(AutopilotZeroTouchPage),
            AutopilotProvisioningMode.InteractiveHardwareHashUpload => typeof(AutopilotInteractiveHashUploadPage),
            _ => typeof(AutopilotJsonProfilePage)
        };
    }

    private Type GetCustomizationPageType()
    {
        FoundryConfigurationDocument configuration = configurationStateService.Current;
        CustomizationSettings settings = configuration.Customization;
        if (configuration.OperatingSystemSelection.IsEnabled)
        {
            return typeof(OsSelectionPage);
        }

        if (settings.MachineNaming.IsEnabled)
        {
            return typeof(MachineNamingPage);
        }

        if (settings.Oobe.IsEnabled)
        {
            return typeof(OobePage);
        }

        if (settings.WindowsOptionalFeatures.IsEnabled)
        {
            return typeof(OptionalFeaturesPage);
        }

        if (settings.AppxRemoval.IsEnabled)
        {
            return typeof(AppRemovalPage);
        }

        return settings.AiComponentRemoval.IsEnabled
            ? typeof(AiComponentsPage)
            : typeof(OsSelectionPage);
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

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(StartMediaViewModel.IsSelectedUsbFoundryMedia))
        {
            return;
        }

        if (DispatcherQueue.HasThreadAccess)
        {
            ApplyUsbActionButtonState();
            return;
        }

        _ = DispatcherQueue.TryEnqueue(ApplyUsbActionButtonState);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ViewModel.Dispose();
        localizationService.LanguageChanged -= OnLanguageChanged;
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
    }
}
