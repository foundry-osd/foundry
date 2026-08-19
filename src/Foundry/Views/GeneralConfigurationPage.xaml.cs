// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

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
        if (!ViewModel.CanCreateMedia)
        {
            return;
        }

        App.Current.NavigationService.NavigateTo(typeof(StartPage));
    }

    private void DeploymentProtectionToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (DeploymentProtectionToggle.IsOn)
        {
            DeploymentProtectionPasswordBox.Focus(FocusState.Programmatic);
            return;
        }

        DeploymentProtectionPasswordBox.Password = string.Empty;
        DeploymentProtectionConfirmationBox.Password = string.Empty;
    }

    private void DeploymentProtectionPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        ViewModel.SetDeploymentProtectionPassword(DeploymentProtectionPasswordBox.Password);
    }

    private void DeploymentProtectionConfirmationBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        ViewModel.SetDeploymentProtectionPasswordConfirmation(DeploymentProtectionConfirmationBox.Password);
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
        ViewModel.RefreshLocalizedText();

        ArchitectureCard.Header = localizationService.GetString("StartMedia.Architecture.Header");
        ArchitectureCard.Description = localizationService.GetString("StartMedia.Architecture.Description");
        SecureBootCard.Header = localizationService.GetString("GeneralConfiguration.SecureBoot.Header");
        SecureBootCard.Description = localizationService.GetString("GeneralConfiguration.SecureBoot.Description");
        Ca2023Toggle.OnContent = localizationService.GetString("StartMedia.Signature.Ca2023");
        Ca2023Toggle.OffContent = localizationService.GetString("StartMedia.Signature.Ca2011");

        WinPeLanguageCard.Header = localizationService.GetString("StartMedia.WinPeLanguage.Header");
        WinPeLanguageCard.Description = localizationService.GetString("StartMedia.WinPeLanguage.Description");
        TimeZoneCard.Header = localizationService.GetString("GeneralConfiguration.TimeZone.Header");
        TimeZoneCard.Description = localizationService.GetString("GeneralConfiguration.TimeZone.Description");
        DeploymentCompletionCard.Header = localizationService.GetString("GeneralConfiguration.Completion.Header");
        DeploymentCompletionCard.Description = localizationService.GetString("GeneralConfiguration.Completion.Description");
        string automaticRebootText = localizationService.GetString("GeneralConfiguration.Completion.AutomaticReboot");
        AutomaticRebootToggle.OnContent = automaticRebootText;
        AutomaticRebootToggle.OffContent = automaticRebootText;
        RebootDelayCard.Header = localizationService.GetString("GeneralConfiguration.Completion.Delay.Header");
        RebootDelayCard.Description = localizationService.GetString("GeneralConfiguration.Completion.Delay.Description");

        DeploymentProtectionCard.Header = localizationService.GetString("GeneralConfiguration.DeploymentProtection.Header");
        DeploymentProtectionCard.Description = localizationService.GetString("GeneralConfiguration.DeploymentProtection.Description");
        string protectionToggleText = localizationService.GetString("GeneralConfiguration.DeploymentProtection.Toggle");
        DeploymentProtectionToggle.OnContent = protectionToggleText;
        DeploymentProtectionToggle.OffContent = protectionToggleText;
        DeploymentProtectionPasswordCard.Header = localizationService.GetString("GeneralConfiguration.DeploymentProtection.PasswordCard.Header");
        DeploymentProtectionPasswordCard.Description = localizationService.GetString("GeneralConfiguration.DeploymentProtection.PasswordCard.Description");
        DeploymentProtectionPasswordBox.PlaceholderText = localizationService.GetString("GeneralConfiguration.DeploymentProtection.Password.Placeholder");
        DeploymentProtectionConfirmationBox.PlaceholderText = localizationService.GetString("GeneralConfiguration.DeploymentProtection.Confirmation.Placeholder");
        DeploymentProtectionValidationText.Text = localizationService.GetString("GeneralConfiguration.DeploymentProtection.Validation");
        DeploymentProtectionRecommendationText.Text = localizationService.GetString("GeneralConfiguration.DeploymentProtection.Recommendation");

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

        ViewModel.RefreshAdkState();
        ViewModel.RefreshTimeZones();

        bool wasInitializingWinPeLanguageSelection = isInitializingWinPeLanguageSelection;
        isInitializingWinPeLanguageSelection = true;
        ViewModel.RefreshWinPeLanguages();
        WinPeLanguageComboBox.SelectedItem = ViewModel.SelectedWinPeLanguage;
        isInitializingWinPeLanguageSelection = wasInitializingWinPeLanguageSelection;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        localizationService.LanguageChanged -= OnLanguageChanged;
        Unloaded -= OnUnloaded;
        ViewModel.Dispose();
    }
}
