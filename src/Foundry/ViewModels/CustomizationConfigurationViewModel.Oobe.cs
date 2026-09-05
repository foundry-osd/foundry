// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;
using Foundry.Services.Configuration;
using Microsoft.UI.Xaml;

namespace Foundry.ViewModels;

public sealed partial class CustomizationConfigurationViewModel
{
    private bool isRefreshingOobeOptions;
    private bool hasOobeAccountValidationIssues;
    private int oobeAccountSecretStateVersion;

    public ObservableCollection<SelectionOption<OobeDiagnosticDataLevel>> OobeDiagnosticDataOptions { get; } = [];

    public ObservableCollection<SelectionOption<OobeLocationAccessMode>> OobeLocationAccessOptions { get; } = [];

    public ObservableCollection<OobeAdditionalAccountEntryViewModel> OobeAdditionalAccounts { get; } = [];

    public bool IsOobeOptionsEnabled => IsOobeEnabled;

    public bool AreOobeAdditionalAccountControlsAvailable => IsOobeEnabled && !IsOobeAdditionalAccountsBlockedByAutopilot;

    public bool HasAdditionalOobeAccounts => OobeAdditionalAccounts.Count > 0;

    public Visibility OobeNoAdministratorWarningVisibility => IsOobeEnabled &&
        !IsOobeAdditionalAccountsBlockedByAutopilot &&
        !EnableAdministratorAccount && HasAdditionalOobeAccounts &&
        OobeAdditionalAccounts.All(entry => entry.Account.Type == OobeAccountType.Standard)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public string OobeSkipUserAccountCreationEffectiveDescription => HasAdditionalOobeAccounts
        ? OobeSkipUserAccountCreationLockedDescription
        : OobeSkipUserAccountCreationDescription;

    public Visibility OobeAdditionalAccountsBlockedVisibility => IsOobeAdditionalAccountsBlockedByAutopilot
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility OobeAccountsNeedsAttentionVisibility => IsOobeEnabled
        && ((IsOobeAdditionalAccountsBlockedByAutopilot && HasAdditionalOobeAccounts) ||
            hasOobeAccountValidationIssues ||
            IsOobeAccountDeploymentProtectionMissing)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Visibility OobeAccountDeploymentProtectionRequiredVisibility => IsOobeAccountDeploymentProtectionMissing
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility OobeAdditionalAccountsEmptyVisibility => HasAdditionalOobeAccounts
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility OobeAdministratorValidationVisibility => ToVisibility(OobeAdministratorValidationMessage);

    public Visibility OobeAdditionalAccountsValidationVisibility => ToVisibility(OobeAdditionalAccountsValidationMessage);

    public int OobeAccountSecretStateVersion
    {
        get => oobeAccountSecretStateVersion;
        private set => SetProperty(ref oobeAccountSecretStateVersion, value);
    }

    [ObservableProperty]
    public partial string OobeAccountsHeader { get; set; }

    [ObservableProperty]
    public partial string OobeAccountsDescription { get; set; }

    [ObservableProperty]
    public partial string OobeAccountsNeedsAttentionText { get; set; }

    [ObservableProperty]
    public partial string OobeAdditionalAccountsBlockedMessage { get; set; }

    [ObservableProperty]
    public partial string OobeAccountsSecurityWarning { get; set; }

    [ObservableProperty]
    public partial string OobeNoAdministratorWarning { get; set; }

    [ObservableProperty]
    public partial string OobeAdministratorAccountLabel { get; set; }

    [ObservableProperty]
    public partial string OobeAdministratorAccountDescription { get; set; }

    [ObservableProperty]
    public partial string OobeAccountPasswordLabel { get; set; }

    [ObservableProperty]
    public partial string OobeAccountPasswordDescription { get; set; }

    [ObservableProperty]
    public partial string OobePasswordPlaceholder { get; set; }

    [ObservableProperty]
    public partial string OobeConfirmationPlaceholder { get; set; }

    [ObservableProperty]
    public partial string OobeSkipUserAccountCreationLabel { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OobeSkipUserAccountCreationEffectiveDescription))]
    public partial string OobeSkipUserAccountCreationDescription { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OobeSkipUserAccountCreationEffectiveDescription))]
    public partial string OobeSkipUserAccountCreationLockedDescription { get; set; }

    [ObservableProperty]
    public partial string OobeAdditionalAccountsLabel { get; set; }

    [ObservableProperty]
    public partial string OobeAdditionalAccountsEmpty { get; set; }

    [ObservableProperty]
    public partial string OobeAdditionalAccountsAddButton { get; set; }

    [ObservableProperty]
    public partial string OobeAdditionalAccountsEditButton { get; set; }

    [ObservableProperty]
    public partial string OobeAdditionalAccountsRemoveButton { get; set; }

    [ObservableProperty]
    public partial string OobeAdministratorValidationMessage { get; set; }

    [ObservableProperty]
    public partial string OobeAdditionalAccountsValidationMessage { get; set; }

    [ObservableProperty]
    public partial string OobeSkipLicenseTermsLabel { get; set; }

    [ObservableProperty]
    public partial string OobeSkipLicenseTermsDescription { get; set; }

    [ObservableProperty]
    public partial string OobeDiagnosticDataLabel { get; set; }

    [ObservableProperty]
    public partial string OobeDiagnosticDataDescription { get; set; }

    [ObservableProperty]
    public partial string OobeHidePrivacySetupLabel { get; set; }

    [ObservableProperty]
    public partial string OobeHidePrivacySetupDescription { get; set; }

    [ObservableProperty]
    public partial string OobeTailoredExperiencesLabel { get; set; }

    [ObservableProperty]
    public partial string OobeTailoredExperiencesDescription { get; set; }

    [ObservableProperty]
    public partial string OobeAdvertisingIdLabel { get; set; }

    [ObservableProperty]
    public partial string OobeAdvertisingIdDescription { get; set; }

    [ObservableProperty]
    public partial string OobeOnlineSpeechRecognitionLabel { get; set; }

    [ObservableProperty]
    public partial string OobeOnlineSpeechRecognitionDescription { get; set; }

    [ObservableProperty]
    public partial string OobeInkingAndTypingDiagnosticsLabel { get; set; }

    [ObservableProperty]
    public partial string OobeInkingAndTypingDiagnosticsDescription { get; set; }

    [ObservableProperty]
    public partial string OobeLocationAccessLabel { get; set; }

    [ObservableProperty]
    public partial string OobeLocationAccessDescription { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOobeOptionsEnabled))]
    [NotifyPropertyChangedFor(nameof(AreOobeAdditionalAccountControlsAvailable))]
    [NotifyPropertyChangedFor(nameof(OobeAccountsNeedsAttentionVisibility))]
    public partial bool IsOobeEnabled { get; set; }

    [ObservableProperty]
    public partial bool EnableAdministratorAccount { get; set; }

    [ObservableProperty]
    public partial bool UseAdministratorPassword { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OobeAdditionalAccountsBlockedVisibility))]
    [NotifyPropertyChangedFor(nameof(AreOobeAdditionalAccountControlsAvailable))]
    [NotifyPropertyChangedFor(nameof(OobeAdministratorValidationVisibility))]
    [NotifyPropertyChangedFor(nameof(OobeAdditionalAccountsValidationVisibility))]
    [NotifyPropertyChangedFor(nameof(OobeAccountsNeedsAttentionVisibility))]
    public partial bool IsOobeAdditionalAccountsBlockedByAutopilot { get; set; }

    [ObservableProperty]
    public partial bool SkipLicenseTerms { get; set; } = true;

    [ObservableProperty]
    public partial SelectionOption<OobeDiagnosticDataLevel>? SelectedOobeDiagnosticData { get; set; }

    [ObservableProperty]
    public partial bool HidePrivacySetup { get; set; } = true;

    [ObservableProperty]
    public partial bool AllowTailoredExperiences { get; set; }

    [ObservableProperty]
    public partial bool AllowAdvertisingId { get; set; }

    [ObservableProperty]
    public partial bool AllowOnlineSpeechRecognition { get; set; }

    [ObservableProperty]
    public partial bool AllowInkingAndTypingDiagnostics { get; set; }

    [ObservableProperty]
    public partial SelectionOption<OobeLocationAccessMode>? SelectedOobeLocationAccess { get; set; }

    public void SetOobeAdministratorPassword(string value)
    {
        oobeAccountSecretStateService.SetAdministratorPassword(value);
    }

    public void SetOobeAdministratorConfirmation(string value)
    {
        oobeAccountSecretStateService.SetAdministratorConfirmation(value);
    }

    public char[] GetOobeAdministratorPasswordCopy()
    {
        return oobeAccountSecretStateService.GetAdministratorPasswordCopy();
    }

    public char[] GetOobeAdministratorConfirmationCopy()
    {
        return oobeAccountSecretStateService.GetAdministratorConfirmationCopy();
    }

    partial void OnIsOobeEnabledChanged(bool value)
    {
        RefreshOobeAccountValidation();
        SaveState();
    }

    partial void OnEnableAdministratorAccountChanged(bool value)
    {
        if (!isApplyingState && UseAdministratorPassword != value)
        {
            UseAdministratorPassword = value;
            return;
        }

        RefreshOobeAccountValidation();
        SaveState();
    }

    partial void OnUseAdministratorPasswordChanged(bool value)
    {
        RefreshOobeAccountValidation();
        SaveState();
    }

    partial void OnSkipLicenseTermsChanged(bool value)
    {
        SaveState();
    }

    partial void OnSelectedOobeDiagnosticDataChanged(SelectionOption<OobeDiagnosticDataLevel>? value)
    {
        if (!isRefreshingOobeOptions)
        {
            SaveState();
        }
    }

    partial void OnHidePrivacySetupChanged(bool value)
    {
        SaveState();
    }

    partial void OnAllowTailoredExperiencesChanged(bool value)
    {
        SaveState();
    }

    partial void OnAllowAdvertisingIdChanged(bool value)
    {
        SaveState();
    }

    partial void OnAllowOnlineSpeechRecognitionChanged(bool value)
    {
        SaveState();
    }

    partial void OnAllowInkingAndTypingDiagnosticsChanged(bool value)
    {
        SaveState();
    }

    partial void OnSelectedOobeLocationAccessChanged(SelectionOption<OobeLocationAccessMode>? value)
    {
        if (!isRefreshingOobeOptions)
        {
            SaveState();
        }
    }

    [RelayCommand]
    private async Task AddOobeAdditionalAccountAsync()
    {
        if (!AreOobeAdditionalAccountControlsAvailable)
        {
            return;
        }

        using OobeAdditionalAccountDialogResult? result = await oobeAdditionalAccountDialogService.ShowAsync(
            account: null,
            existingAccounts: OobeAdditionalAccounts.Select(entry => entry.Account).ToArray(),
            initialPassword: [],
            initialConfirmation: []);
        if (result is null)
        {
            return;
        }

        OobeAdditionalAccounts.Add(CreateOobeAdditionalAccountEntry(result.Account));
        ApplyAdditionalAccountSecrets(result);
        RefreshOobeAdditionalAccountsState();
        RefreshOobeAccountValidation();
        SaveState();
    }

    private async Task EditOobeAdditionalAccountAsync(OobeAdditionalAccountEntryViewModel entry)
    {
        if (!IsOobeEnabled)
        {
            return;
        }

        char[] password = oobeAccountSecretStateService.GetAdditionalAccountPasswordCopy(entry.Id);
        char[] confirmation = oobeAccountSecretStateService.GetAdditionalAccountConfirmationCopy(entry.Id);
        try
        {
            using OobeAdditionalAccountDialogResult? result = await oobeAdditionalAccountDialogService.ShowAsync(
                entry.Account,
                OobeAdditionalAccounts.Select(account => account.Account).ToArray(),
                password,
                confirmation);
            if (result is null)
            {
                return;
            }

            entry.Account = result.Account;
            entry.RefreshPresentation(
                GetOobeAccountTypeDisplayName(result.Account.Type),
                OobeAdditionalAccountsEditButton,
                OobeAdditionalAccountsRemoveButton);
            ApplyAdditionalAccountSecrets(result);
            RefreshOobeAdditionalAccountsState();
            RefreshOobeAccountValidation();
            SaveState();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(password.AsSpan()));
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(confirmation.AsSpan()));
        }
    }

    private void RemoveOobeAdditionalAccount(OobeAdditionalAccountEntryViewModel entry)
    {
        if (!IsOobeEnabled)
        {
            return;
        }

        OobeAdditionalAccounts.Remove(entry);
        RefreshOobeAdditionalAccountsState();
        RefreshOobeAccountValidation();
        SaveState();
    }

    private void ApplyOobeState(OobeSettings settings)
    {
        IsOobeEnabled = settings.IsEnabled;
        UseAdministratorPassword = settings.UseAdministratorPassword;
        EnableAdministratorAccount = settings.EnableAdministratorAccount;
        SkipLicenseTerms = settings.SkipLicenseTerms;
        IsOobeAdditionalAccountsBlockedByAutopilot = configurationStateService.IsAutopilotEnabled;
        SelectedOobeDiagnosticData = SelectOption(OobeDiagnosticDataOptions, settings.DiagnosticDataLevel);
        HidePrivacySetup = settings.HidePrivacySetup;
        AllowTailoredExperiences = settings.AllowTailoredExperiences;
        AllowAdvertisingId = settings.AllowAdvertisingId;
        AllowOnlineSpeechRecognition = settings.AllowOnlineSpeechRecognition;
        AllowInkingAndTypingDiagnostics = settings.AllowInkingAndTypingDiagnostics;
        SelectedOobeLocationAccess = SelectOption(OobeLocationAccessOptions, settings.LocationAccess);

        OobeAdditionalAccounts.Clear();
        foreach (OobeAdditionalAccountSettings account in settings.AdditionalAccounts)
        {
            OobeAdditionalAccounts.Add(CreateOobeAdditionalAccountEntry(account));
        }

        RefreshOobeAdditionalAccountsState();
        RefreshOobeAccountValidation();
    }

    private OobeSettings BuildOobeSettings()
    {
        return IsOobeEnabled
            ? new OobeSettings
            {
                IsEnabled = true,
                EnableAdministratorAccount = EnableAdministratorAccount,
                UseAdministratorPassword = UseAdministratorPassword,
                SkipLicenseTerms = SkipLicenseTerms,
                DiagnosticDataLevel = SelectedOobeDiagnosticData?.Value ?? OobeDiagnosticDataLevel.Required,
                HidePrivacySetup = HidePrivacySetup,
                AllowTailoredExperiences = AllowTailoredExperiences,
                AllowAdvertisingId = AllowAdvertisingId,
                AllowOnlineSpeechRecognition = AllowOnlineSpeechRecognition,
                AllowInkingAndTypingDiagnostics = AllowInkingAndTypingDiagnostics,
                LocationAccess = SelectedOobeLocationAccess?.Value ?? OobeLocationAccessMode.UserControlled,
                AdditionalAccounts = OobeAdditionalAccounts.Select(entry => entry.Account).ToArray()
            }
            : new OobeSettings();
    }

    private void RefreshOobeLocalizedText()
    {
        OobeAccountsHeader = localizationService.GetString("Customization.OobeAccountsHeader");
        OobeAccountsDescription = localizationService.GetString("Customization.OobeAccountsDescription");
        OobeAccountsNeedsAttentionText = localizationService.GetString("Common.NeedsAttention");
        OobeAdditionalAccountsBlockedMessage = localizationService.GetString("Customization.OobeAdditionalAccountsBlockedMessage");
        OobeAccountsSecurityWarning = localizationService.GetString("Customization.OobeAccountsSecurityWarning");
        OobeNoAdministratorWarning = localizationService.GetString("Customization.OobeNoAdministratorWarning");
        OobeAdministratorAccountLabel = localizationService.GetString("Customization.OobeAdministratorAccountLabel");
        OobeAdministratorAccountDescription = localizationService.GetString("Customization.OobeAdministratorAccountDescription");
        OobeAccountPasswordLabel = localizationService.GetString("Customization.OobeAccountPasswordLabel");
        OobeAccountPasswordDescription = localizationService.GetString("Customization.OobeAccountPasswordDescription");
        OobePasswordPlaceholder = localizationService.GetString("GeneralConfiguration.DeploymentProtection.Password.Placeholder");
        OobeConfirmationPlaceholder = localizationService.GetString("GeneralConfiguration.DeploymentProtection.Confirmation.Placeholder");
        OobeSkipUserAccountCreationLabel = localizationService.GetString("Customization.OobeSkipUserAccountCreationLabel");
        OobeSkipUserAccountCreationDescription = localizationService.GetString("Customization.OobeSkipUserAccountCreationDescription");
        OobeSkipUserAccountCreationLockedDescription = localizationService.GetString("Customization.OobeSkipUserAccountCreationLockedDescription");
        OobeAdditionalAccountsLabel = localizationService.GetString("Customization.OobeAdditionalAccountsLabel");
        OobeAdditionalAccountsEmpty = localizationService.GetString("Customization.OobeAdditionalAccountsEmpty");
        OobeAdditionalAccountsAddButton = localizationService.GetString("Customization.OobeAdditionalAccountsAddButton");
        OobeAdditionalAccountsEditButton = localizationService.GetString("Customization.OobeAdditionalAccountsEditButton");
        OobeAdditionalAccountsRemoveButton = localizationService.GetString("Customization.OobeAdditionalAccountsRemoveButton");
        OobeSkipLicenseTermsLabel = localizationService.GetString("Customization.OobeSkipLicenseTermsLabel");
        OobeSkipLicenseTermsDescription = localizationService.GetString("Customization.OobeSkipLicenseTermsDescription");
        OobeDiagnosticDataLabel = localizationService.GetString("Customization.OobeDiagnosticDataLabel");
        OobeDiagnosticDataDescription = localizationService.GetString("Customization.OobeDiagnosticDataDescription");
        OobeHidePrivacySetupLabel = localizationService.GetString("Customization.OobeHidePrivacySetupLabel");
        OobeHidePrivacySetupDescription = localizationService.GetString("Customization.OobeHidePrivacySetupDescription");
        OobeTailoredExperiencesLabel = localizationService.GetString("Customization.OobeTailoredExperiencesLabel");
        OobeTailoredExperiencesDescription = localizationService.GetString("Customization.OobeTailoredExperiencesDescription");
        OobeAdvertisingIdLabel = localizationService.GetString("Customization.OobeAdvertisingIdLabel");
        OobeAdvertisingIdDescription = localizationService.GetString("Customization.OobeAdvertisingIdDescription");
        OobeOnlineSpeechRecognitionLabel = localizationService.GetString("Customization.OobeOnlineSpeechRecognitionLabel");
        OobeOnlineSpeechRecognitionDescription = localizationService.GetString("Customization.OobeOnlineSpeechRecognitionDescription");
        OobeInkingAndTypingDiagnosticsLabel = localizationService.GetString("Customization.OobeInkingAndTypingDiagnosticsLabel");
        OobeInkingAndTypingDiagnosticsDescription = localizationService.GetString("Customization.OobeInkingAndTypingDiagnosticsDescription");
        OobeLocationAccessLabel = localizationService.GetString("Customization.OobeLocationAccessLabel");
        OobeLocationAccessDescription = localizationService.GetString("Customization.OobeLocationAccessDescription");

        foreach (OobeAdditionalAccountEntryViewModel account in OobeAdditionalAccounts)
        {
            account.RefreshPresentation(
                GetOobeAccountTypeDisplayName(account.Account.Type),
                OobeAdditionalAccountsEditButton,
                OobeAdditionalAccountsRemoveButton);
        }

        RefreshOobeAccountValidation();
        RefreshOobeOptions();
    }

    private void RefreshOobeOptions()
    {
        OobeDiagnosticDataLevel selectedDiagnosticData = SelectedOobeDiagnosticData?.Value ?? OobeDiagnosticDataLevel.Required;
        OobeLocationAccessMode selectedLocationAccess = SelectedOobeLocationAccess?.Value ?? OobeLocationAccessMode.UserControlled;

        isRefreshingOobeOptions = true;
        try
        {
            OobeDiagnosticDataOptions.Clear();
            OobeDiagnosticDataOptions.Add(new(OobeDiagnosticDataLevel.Required, localizationService.GetString("Customization.OobeDiagnosticDataRequired")));
            OobeDiagnosticDataOptions.Add(new(OobeDiagnosticDataLevel.Optional, localizationService.GetString("Customization.OobeDiagnosticDataOptional")));
            OobeDiagnosticDataOptions.Add(new(OobeDiagnosticDataLevel.Off, localizationService.GetString("Customization.OobeDiagnosticDataOff")));
            SelectedOobeDiagnosticData = SelectOption(OobeDiagnosticDataOptions, selectedDiagnosticData) ?? OobeDiagnosticDataOptions[0];

            OobeLocationAccessOptions.Clear();
            OobeLocationAccessOptions.Add(new(OobeLocationAccessMode.UserControlled, localizationService.GetString("Customization.OobeLocationUserControlled")));
            OobeLocationAccessOptions.Add(new(OobeLocationAccessMode.ForceOff, localizationService.GetString("Customization.OobeLocationForceOff")));
            SelectedOobeLocationAccess = SelectOption(OobeLocationAccessOptions, selectedLocationAccess) ?? OobeLocationAccessOptions[0];
        }
        finally
        {
            isRefreshingOobeOptions = false;
        }
    }

    private void RefreshOobeAdditionalAccountsState()
    {
        OnPropertyChanged(nameof(HasAdditionalOobeAccounts));
        OnPropertyChanged(nameof(OobeSkipUserAccountCreationEffectiveDescription));
        OnPropertyChanged(nameof(OobeAdditionalAccountsEmptyVisibility));
    }

    private void RefreshOobeAccountValidation()
    {
        OobeSettings settings = BuildOobeSettings();
        OobeAccountConfigurationValidationResult validation = oobeAccountSecretStateService.Validate(settings);
        hasOobeAccountValidationIssues = !validation.IsValid;
        OobeAdministratorValidationMessage = validation.Issues
            .FirstOrDefault(issue => issue.IsAdministratorAccount) is { } administratorIssue
            ? OobeAccountValidationTextFormatter.FormatAdministratorIssue(localizationService, administratorIssue)
            : string.Empty;
        OobeAdditionalAccountsValidationMessage = validation.Issues
            .FirstOrDefault(issue => !issue.IsAdministratorAccount) is { } accountIssue
            ? OobeAccountValidationTextFormatter.FormatAdditionalAccountIssue(localizationService, accountIssue)
            : string.Empty;

        OnPropertyChanged(nameof(OobeAdministratorValidationVisibility));
        OnPropertyChanged(nameof(OobeAdditionalAccountsValidationVisibility));
        OnPropertyChanged(nameof(OobeAccountDeploymentProtectionRequiredVisibility));
        OnPropertyChanged(nameof(OobeNoAdministratorWarningVisibility));
        OnPropertyChanged(nameof(OobeAccountsNeedsAttentionVisibility));
    }

    private bool IsOobeAccountDeploymentProtectionMissing =>
        OobeAccountConfigurationValidator.RequiresProtectedMedia(BuildOobeSettings()) &&
        !configurationStateService.Current.General.DeploymentProtection.IsEnabled;

    private void ApplyAdditionalAccountSecrets(OobeAdditionalAccountDialogResult result)
    {
        oobeAccountSecretStateService.SetAdditionalAccountPassword(result.Account.Id, result.Password);
        oobeAccountSecretStateService.SetAdditionalAccountConfirmation(result.Account.Id, result.Confirmation);
    }

    private OobeAdditionalAccountEntryViewModel CreateOobeAdditionalAccountEntry(OobeAdditionalAccountSettings account)
    {
        return new OobeAdditionalAccountEntryViewModel(
            account,
            GetOobeAccountTypeDisplayName(account.Type),
            OobeAdditionalAccountsEditButton,
            OobeAdditionalAccountsRemoveButton,
            EditOobeAdditionalAccountAsync,
            RemoveOobeAdditionalAccount);
    }

    private string GetOobeAccountTypeDisplayName(OobeAccountType type)
    {
        return localizationService.GetString(type == OobeAccountType.Administrator
            ? "Customization.OobeAccountTypeAdministrator"
            : "Customization.OobeAccountTypeStandard");
    }

    private static SelectionOption<T>? SelectOption<T>(IEnumerable<SelectionOption<T>> options, T value)
        where T : struct, Enum
    {
        return options.FirstOrDefault(option => EqualityComparer<T>.Default.Equals(option.Value, value));
    }

    private static Visibility ToVisibility(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? Visibility.Collapsed : Visibility.Visible;
    }
}
