// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Application;
using Foundry.Core.Services.Configuration;
using Foundry.Services.Configuration;
using Foundry.Services.Localization;
using Microsoft.UI.Xaml;

namespace Foundry.ViewModels;

/// <summary>
/// Backs deployment customization settings that are generated into the Foundry configuration.
/// </summary>
public sealed partial class CustomizationConfigurationViewModel : ObservableObject, IDisposable
{
    private readonly IFoundryConfigurationStateService configurationStateService;
    private readonly IDialogService dialogService;
    private readonly IApplicationLocalizationService localizationService;
    private readonly ILanguageRegistryService languageRegistryService;
    private readonly IOobeAccountSecretStateService oobeAccountSecretStateService;
    private readonly IOobeAdditionalAccountDialogService oobeAdditionalAccountDialogService;
    private readonly ManagedConfigurationViewModel managed;
    private readonly HashSet<CustomizationCatalog> initializedCatalogs = [];
    private bool isApplyingState = true;
    private bool isSavingState;

    public CustomizationConfigurationViewModel(
        IFoundryConfigurationStateService configurationStateService,
        ILanguageRegistryService languageRegistryService,
        IApplicationLocalizationService localizationService,
        IDialogService dialogService,
        IOobeAccountSecretStateService oobeAccountSecretStateService,
        IOobeAdditionalAccountDialogService oobeAdditionalAccountDialogService,
        ManagedConfigurationViewModel managed)
    {
        this.managed = managed;
        this.configurationStateService = configurationStateService;
        this.languageRegistryService = languageRegistryService;
        this.localizationService = localizationService;
        this.dialogService = dialogService;
        this.oobeAccountSecretStateService = oobeAccountSecretStateService;
        this.oobeAdditionalAccountDialogService = oobeAdditionalAccountDialogService;

        RefreshLocalizedText();
        ApplyState(
            configurationStateService.Current.Customization,
            configurationStateService.Current.OperatingSystemSelection);

        localizationService.LanguageChanged += OnLanguageChanged;
        configurationStateService.StateChanged += OnConfigurationStateChanged;
        managed.PropertyChanged += OnManagedChanged;
        oobeAccountSecretStateService.Changed += OnOobeAccountSecretStateChanged;
        isApplyingState = false;
    }

    /// <summary>
    /// Initializes only the large catalog required by the page being opened.
    /// </summary>
    public void InitializeSection(ConfigurationNavigationTarget target)
    {
        CustomizationCatalog catalog = CustomizationCatalogResolver.Resolve(target);
        if (catalog == CustomizationCatalog.None || !initializedCatalogs.Add(catalog))
        {
            return;
        }

        isApplyingState = true;
        try
        {
            switch (catalog)
            {
                case CustomizationCatalog.OperatingSystemSelection:
                    InitializeOperatingSystemSelectionOptions(languageRegistryService.GetLanguages());
                    ApplyOperatingSystemSelectionState(configurationStateService.Current.OperatingSystemSelection);
                    RefreshOperatingSystemSelectionLocalizedText();
                    break;
                case CustomizationCatalog.WindowsOptionalFeatures:
                    InitializeWindowsOptionalFeatureCatalog();
                    ApplyWindowsOptionalFeatureState(configurationStateService.Current.Customization.WindowsOptionalFeatures);
                    RefreshWindowsOptionalFeatureLocalizedText();
                    break;
                case CustomizationCatalog.AppxRemoval:
                    InitializeAppxRemovalCatalog();
                    ApplyAppxRemovalState(configurationStateService.Current.Customization.AppxRemoval);
                    RefreshAppxRemovalLocalizedText();
                    break;
            }
        }
        catch
        {
            initializedCatalogs.Remove(catalog);
            throw;
        }
        finally
        {
            isApplyingState = false;
        }
    }

    public string OperatingSystemDocumentationUrl => FoundryApplicationInfo.OperatingSystemDocumentationUrl;

    public string MachineNamingDocumentationUrl => FoundryApplicationInfo.MachineNamingDocumentationUrl;

    public string OobeDocumentationUrl => FoundryApplicationInfo.OobeDocumentationUrl;

    public string OptionalFeaturesDocumentationUrl => FoundryApplicationInfo.OptionalFeaturesDocumentationUrl;

    public string AppRemovalDocumentationUrl => FoundryApplicationInfo.AppRemovalDocumentationUrl;

    public string AiComponentsDocumentationUrl => FoundryApplicationInfo.AiComponentsDocumentationUrl;

    /// <summary>
    /// Releases subscriptions to localization and shared configuration state.
    /// </summary>
    public void Dispose()
    {
        localizationService.LanguageChanged -= OnLanguageChanged;
        configurationStateService.StateChanged -= OnConfigurationStateChanged;
        managed.PropertyChanged -= OnManagedChanged;
        oobeAccountSecretStateService.Changed -= OnOobeAccountSecretStateChanged;
        foreach (AppxRemovalItemViewModel item in AppxRemovalCategories.SelectMany(category => category.Items))
        {
            item.PropertyChanged -= OnAppxRemovalItemPropertyChanged;
        }

        foreach (SelectableStringOptionViewModel option in OperatingSystemLanguageOptions
                     .Concat(OperatingSystemReleaseOptions)
                     .Concat(OperatingSystemLicenseChannelOptions)
                     .Concat(OperatingSystemEditionOptions))
        {
            option.PropertyChanged -= OnOperatingSystemSelectionOptionPropertyChanged;
        }
    }

    private void ApplyState(CustomizationSettings settings, OperatingSystemSelectionSettings operatingSystemSelection)
    {
        isApplyingState = true;
        try
        {
            ApplyOperatingSystemSelectionState(operatingSystemSelection);
            ApplyMachineNamingState(settings.MachineNaming);
            ApplyOobeState(settings.Oobe);
            ApplyAiComponentRemovalState(settings.AiComponentRemoval);
            ApplyWindowsOptionalFeatureState(settings.WindowsOptionalFeatures);
            ApplyAppxRemovalState(settings.AppxRemoval);
        }
        finally
        {
            isApplyingState = false;
        }
    }

    private void SaveState()
    {
        // A managed configuration is read-only and these sections hold no per-PC value: nothing is saved, not even a normalization.
        if (isApplyingState || isRefreshingOperatingSystemSelectionOptions || configurationStateService.IsManaged)
        {
            return;
        }

        isSavingState = true;
        try
        {
            configurationStateService.UpdateCustomization(new CustomizationSettings
            {
                MachineNaming = BuildMachineNamingSettings(),
                Oobe = BuildOobeSettings(),
                AiComponentRemoval = BuildAiComponentRemovalSettings(),
                WindowsOptionalFeatures = BuildWindowsOptionalFeatureSettings(),
                AppxRemoval = BuildAppxRemovalSettings()
            });
            configurationStateService.UpdateOperatingSystemSelection(BuildOperatingSystemSelectionSettings());
        }
        finally
        {
            isSavingState = false;
        }
    }

    private void SaveWindowsOptionalFeatureState()
    {
        if (isApplyingState || configurationStateService.IsManaged)
        {
            return;
        }

        isSavingState = true;
        try
        {
            configurationStateService.UpdateCustomization(new CustomizationSettings
            {
                MachineNaming = BuildMachineNamingSettings(),
                Oobe = BuildOobeSettings(),
                AiComponentRemoval = BuildAiComponentRemovalSettings(),
                WindowsOptionalFeatures = BuildWindowsOptionalFeatureSettings(),
                AppxRemoval = BuildAppxRemovalSettings()
            });
        }
        finally
        {
            isSavingState = false;
        }
    }

    private void RefreshLocalizedText()
    {
        RefreshMachineNamingLocalizedText();
        RefreshOperatingSystemSelectionLocalizedText();
        RefreshOobeLocalizedText();
        RefreshAiComponentRemovalLocalizedText();
        RefreshWindowsOptionalFeatureLocalizedText();
        RefreshAppxRemovalLocalizedText();
        RaiseMachineNamingPropertiesChanged();
    }

    private void OnLanguageChanged(object? sender, ApplicationLanguageChangedEventArgs e)
    {
        RefreshLocalizedText();
    }

    /// <summary>
    /// A managed configuration keeps its published accounts: they cannot be removed, and only their passwords can be edited, when
    /// the publication does not include them.
    /// </summary>
    private void OnManagedChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        foreach (OobeAdditionalAccountEntryViewModel entry in OobeAdditionalAccounts)
        {
            entry.EditCommand.NotifyCanExecuteChanged();
            entry.RemoveCommand.NotifyCanExecuteChanged();
        }
    }

    private void OnConfigurationStateChanged(object? sender, EventArgs e)
    {
        if (isSavingState)
        {
            return;
        }

        ApplyState(
            configurationStateService.Current.Customization,
            configurationStateService.Current.OperatingSystemSelection);
    }

    private void OnOobeAccountSecretStateChanged(object? sender, EventArgs e)
    {
        RefreshOobeAccountValidation();
        OobeAccountSecretStateVersion++;
    }

}
