// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.ObjectModel;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Application;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.Files;
using Foundry.Services.Configuration;
using Foundry.Services.Localization;
using Microsoft.UI.Xaml;
using Serilog;

namespace Foundry.ViewModels;

/// <summary>
/// Backs the network configuration page and keeps persisted 802.1X/Wi-Fi settings in sync with the UI.
/// </summary>
/// <remarks>
/// Selected profiles and certificates are imported into the <see cref="AuthoringFileLibrary"/> and referenced by content hash.
/// Passphrases and PFX passwords are routed through <see cref="INetworkSecretStateService"/> so they can be used for
/// provisioning without entering the configuration.
/// </remarks>
public sealed partial class NetworkConfigurationViewModel : ObservableObject, IDisposable
{
    private readonly IFoundryConfigurationStateService configurationStateService;
    private readonly INetworkSecretStateService networkSecretStateService;
    private readonly IFilePickerService filePickerService;
    private readonly IDialogService dialogService;
    private readonly AuthoringFileLibrary authoringFiles;
    private readonly IApplicationLocalizationService localizationService;
    private readonly ILogger logger;
    private bool isApplyingState = true;
    private bool isSavingState;
    private AuthoringFileReference? dot1xProfileTemplate;
    private AuthoringFileReference? dot1xCertificate;
    private AuthoringFileReference? wifiEnterpriseProfileTemplate;
    private AuthoringFileReference? wifiCertificate;

    [ObservableProperty]
    public partial string CertificatePasswordLabel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Dot1xCertificatePassword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string WifiCertificatePassword { get; set; } = string.Empty;

    partial void OnDot1xCertificatePasswordChanged(string value) => SaveState();
    partial void OnWifiCertificatePasswordChanged(string value) => SaveState();

    public NetworkConfigurationViewModel(
        IFoundryConfigurationStateService configurationStateService,
        INetworkSecretStateService networkSecretStateService,
        IFilePickerService filePickerService,
        IDialogService dialogService,
        AuthoringFileLibrary authoringFiles,
        IApplicationLocalizationService localizationService,
        ILogger logger)
    {
        this.configurationStateService = configurationStateService;
        this.networkSecretStateService = networkSecretStateService;
        this.filePickerService = filePickerService;
        this.dialogService = dialogService;
        this.authoringFiles = authoringFiles;
        this.localizationService = localizationService;
        this.logger = logger.ForContext<NetworkConfigurationViewModel>();

        RefreshLocalizedText();
        RefreshWifiSecurityTypes();
        ApplyState(configurationStateService.Current.Network);

        localizationService.LanguageChanged += OnLanguageChanged;
        configurationStateService.StateChanged += OnConfigurationStateChanged;
        isApplyingState = false;
    }

    /// <summary>
    /// Gets the Wi-Fi security type options shown by the network page.
    /// </summary>
    public ObservableCollection<SelectionOption<string>> WifiSecurityTypes { get; } = [];

    [ObservableProperty]
    public partial string ProfileTemplateLabel { get; set; }

    [ObservableProperty]
    public partial string Dot1xCertificateLabel { get; set; }

    [ObservableProperty]
    public partial string RequiresCertificateText { get; set; }

    [ObservableProperty]
    public partial string WifiConfiguredText { get; set; }

    [ObservableProperty]
    public partial string WifiSsidLabel { get; set; }

    [ObservableProperty]
    public partial string WifiSecurityTypeLabel { get; set; }

    [ObservableProperty]
    public partial string WifiPassphraseLabel { get; set; }

    [ObservableProperty]
    public partial string WifiPersonalDescription { get; set; }

    [ObservableProperty]
    public partial string WifiEnterpriseHeader { get; set; }

    [ObservableProperty]
    public partial string WifiEnterpriseDescription { get; set; }

    [ObservableProperty]
    public partial string WifiEnterpriseTemplateText { get; set; }

    [ObservableProperty]
    public partial string WifiCertificateLabel { get; set; }

    [ObservableProperty]
    public partial string WiredDot1xWindowsRoamingHeader { get; set; }

    [ObservableProperty]
    public partial string WiredDot1xWindowsRoamingDescription { get; set; }

    [ObservableProperty]
    public partial string WifiWindowsRoamingHeader { get; set; }

    [ObservableProperty]
    public partial string WifiWindowsRoamingDescription { get; set; }

    [ObservableProperty]
    public partial string RoamNetworkProfilesText { get; set; }

    [ObservableProperty]
    public partial string RoamPrivateKeyMaterialText { get; set; }

    [ObservableProperty]
    public partial string RoamPrivateKeyMaterialDescription { get; set; }

    [ObservableProperty]
    public partial string BrowseButtonText { get; set; }

    [ObservableProperty]
    public partial bool IsWiredDot1xWindowsRoamingExpanded { get; set; }

    [ObservableProperty]
    public partial bool IsWifiWindowsRoamingExpanded { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDot1xSectionEnabled))]
    [NotifyPropertyChangedFor(nameof(IsDot1xCertificatePathEnabled))]
    [NotifyPropertyChangedFor(nameof(IsWiredDot1xProfileRoamingAvailable))]
    [NotifyPropertyChangedFor(nameof(IsWiredDot1xPrivateKeyMaterialRoamingEnabled))]
    [NotifyPropertyChangedFor(nameof(WiredDot1xRoamPrivateKeyMaterialVisibility))]
    [NotifyPropertyChangedFor(nameof(Dot1xValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasDot1xValidationError))]
    [NotifyPropertyChangedFor(nameof(Dot1xValidationVisibility))]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasValidationError))]
    public partial bool IsDot1xEnabled { get; set; }

    /// <summary>Gets the file name of the selected wired profile template.</summary>
    public string Dot1xProfileTemplateFileName => dot1xProfileTemplate?.FileName ?? string.Empty;

    /// <summary>Gets the file name of the selected wired certificate.</summary>
    public string Dot1xCertificateFileName => dot1xCertificate?.FileName ?? string.Empty;

    /// <summary>Gets the file name of the selected enterprise Wi-Fi profile template.</summary>
    public string WifiEnterpriseProfileTemplateFileName => wifiEnterpriseProfileTemplate?.FileName ?? string.Empty;

    /// <summary>Gets the file name of the selected Wi-Fi certificate.</summary>
    public string WifiCertificateFileName => wifiCertificate?.FileName ?? string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDot1xCertificatePathEnabled))]
    [NotifyPropertyChangedFor(nameof(Dot1xValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasDot1xValidationError))]
    [NotifyPropertyChangedFor(nameof(Dot1xValidationVisibility))]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasValidationError))]
    public partial bool IsDot1xCertificateRequired { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWifiConfigurationSectionEnabled))]
    [NotifyPropertyChangedFor(nameof(IsWifiSecuritySelectionEnabled))]
    [NotifyPropertyChangedFor(nameof(IsWifiPersonalSectionEnabled))]
    [NotifyPropertyChangedFor(nameof(IsWifiEnterpriseSectionEnabled))]
    [NotifyPropertyChangedFor(nameof(IsWifiCertificatePathEnabled))]
    [NotifyPropertyChangedFor(nameof(IsWifiProfileRoamingAvailable))]
    [NotifyPropertyChangedFor(nameof(IsWifiPrivateKeyMaterialRoamingEnabled))]
    [NotifyPropertyChangedFor(nameof(WifiRoamPrivateKeyMaterialVisibility))]
    [NotifyPropertyChangedFor(nameof(WifiValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasWifiValidationError))]
    [NotifyPropertyChangedFor(nameof(WifiValidationVisibility))]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasValidationError))]
    public partial bool IsWifiProvisioned { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWifiConfigurationSectionEnabled))]
    [NotifyPropertyChangedFor(nameof(IsWifiSecuritySelectionEnabled))]
    [NotifyPropertyChangedFor(nameof(IsWifiPersonalSectionEnabled))]
    [NotifyPropertyChangedFor(nameof(IsWifiEnterpriseSectionEnabled))]
    [NotifyPropertyChangedFor(nameof(IsWifiCertificatePathEnabled))]
    [NotifyPropertyChangedFor(nameof(WifiValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasWifiValidationError))]
    [NotifyPropertyChangedFor(nameof(WifiValidationVisibility))]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasValidationError))]
    public partial bool IsWifiConfigured { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WifiValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasWifiValidationError))]
    [NotifyPropertyChangedFor(nameof(WifiValidationVisibility))]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasValidationError))]
    public partial string WifiSsid { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWifiPersonalSectionEnabled))]
    [NotifyPropertyChangedFor(nameof(IsWifiEnterpriseSectionEnabled))]
    [NotifyPropertyChangedFor(nameof(IsWifiCertificatePathEnabled))]
    [NotifyPropertyChangedFor(nameof(WifiValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasWifiValidationError))]
    [NotifyPropertyChangedFor(nameof(WifiValidationVisibility))]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasValidationError))]
    public partial SelectionOption<string>? SelectedWifiSecurityType { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WifiValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasWifiValidationError))]
    [NotifyPropertyChangedFor(nameof(WifiValidationVisibility))]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasValidationError))]
    public partial string WifiPassphrase { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWifiCertificatePathEnabled))]
    [NotifyPropertyChangedFor(nameof(WifiValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasWifiValidationError))]
    [NotifyPropertyChangedFor(nameof(WifiValidationVisibility))]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasValidationError))]
    public partial bool IsWifiCertificateRequired { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWiredDot1xPrivateKeyMaterialRoamingEnabled))]
    [NotifyPropertyChangedFor(nameof(WiredDot1xRoamPrivateKeyMaterialVisibility))]
    public partial bool IsWiredDot1xProfileRoamingEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsWiredDot1xPrivateKeyMaterialRoamingRequested { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWifiPrivateKeyMaterialRoamingEnabled))]
    [NotifyPropertyChangedFor(nameof(WifiRoamPrivateKeyMaterialVisibility))]
    public partial bool IsWifiProfileRoamingEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsWifiPrivateKeyMaterialRoamingRequested { get; set; }

    public bool IsDot1xSectionEnabled => IsDot1xEnabled;
    public bool IsDot1xCertificatePathEnabled => IsDot1xEnabled && IsDot1xCertificateRequired;
    public bool IsWifiConfigurationSectionEnabled => IsWifiProvisioned && IsWifiConfigured;
    public bool IsWifiSecuritySelectionEnabled => IsWifiConfigurationSectionEnabled;
    public bool IsWifiPersonalSectionEnabled => IsWifiConfigurationSectionEnabled && IsWifiPersonalSelected;
    public bool IsWifiEnterpriseSectionEnabled => IsWifiConfigurationSectionEnabled && IsWifiEnterpriseSelected;
    public bool IsWifiCertificatePathEnabled => IsWifiEnterpriseSectionEnabled && IsWifiCertificateRequired;
    public bool IsWifiPersonalSelected => string.Equals(SelectedWifiSecurityType?.Value, NetworkConfigurationValidator.WifiSecurityPersonal, StringComparison.OrdinalIgnoreCase);
    public bool IsWifiEnterpriseSelected => NetworkConfigurationValidator.IsEnterpriseSecurityType(SelectedWifiSecurityType?.Value);
    public bool IsWiredDot1xProfileRoamingAvailable => IsDot1xEnabled;
    public bool IsWifiProfileRoamingAvailable => IsWifiProvisioned;
    public bool IsWiredDot1xPrivateKeyMaterialRoamingEnabled => IsWiredDot1xProfileRoamingAvailable && IsWiredDot1xProfileRoamingEnabled;
    public bool IsWifiPrivateKeyMaterialRoamingEnabled => IsWifiProfileRoamingAvailable && IsWifiProfileRoamingEnabled;
    public bool HasDot1xValidationError => !string.IsNullOrWhiteSpace(Dot1xValidationMessage);
    public string Dot1xValidationMessage => FormatValidationMessage(NetworkConfigurationValidator.Validate(BuildDot1xOnlySettings(), CreateValidationInputs(), configurationStateService.IsMediaProtected), dot1xOnly: true);
    public Visibility Dot1xValidationVisibility => HasDot1xValidationError ? Visibility.Visible : Visibility.Collapsed;
    public bool HasWifiValidationError => !string.IsNullOrWhiteSpace(WifiValidationMessage);
    public string WifiValidationMessage => FormatValidationMessage(NetworkConfigurationValidator.Validate(BuildWifiOnlySettings(), CreateValidationInputs(), configurationStateService.IsMediaProtected), dot1xOnly: false);
    public Visibility WifiValidationVisibility => HasWifiValidationError ? Visibility.Visible : Visibility.Collapsed;
    public bool HasValidationError => HasDot1xValidationError || HasWifiValidationError;

    public string WifiDocumentationUrl => FoundryApplicationInfo.WifiDocumentationUrl;

    public string EthernetDot1xDocumentationUrl => FoundryApplicationInfo.EthernetDot1xDocumentationUrl;
    public string ValidationMessage => HasDot1xValidationError ? Dot1xValidationMessage : WifiValidationMessage;
    public Visibility Dot1xCertificatePathVisibility => IsDot1xCertificatePathEnabled ? Visibility.Visible : Visibility.Collapsed;
    public Visibility WifiConfigurationFieldsVisibility => IsWifiConfigurationSectionEnabled ? Visibility.Visible : Visibility.Collapsed;
    public Visibility WifiPersonalSettingsVisibility => IsWifiPersonalSectionEnabled ? Visibility.Visible : Visibility.Collapsed;
    public Visibility WifiEnterpriseSettingsVisibility => IsWifiEnterpriseSectionEnabled ? Visibility.Visible : Visibility.Collapsed;
    public Visibility WifiCertificatePathVisibility => IsWifiCertificatePathEnabled ? Visibility.Visible : Visibility.Collapsed;
    public Visibility WiredDot1xRoamPrivateKeyMaterialVisibility => IsWiredDot1xPrivateKeyMaterialRoamingEnabled ? Visibility.Visible : Visibility.Collapsed;
    public Visibility WifiRoamPrivateKeyMaterialVisibility => IsWifiPrivateKeyMaterialRoamingEnabled ? Visibility.Visible : Visibility.Collapsed;
    public string Dot1xProfileTemplateValidationMessage => FormatFieldValidationMessage(
        NetworkConfigurationValidator.Validate(BuildDot1xOnlySettings(), CreateValidationInputs(), configurationStateService.IsMediaProtected),
        NetworkConfigurationValidationCode.WiredProfileTemplateRequired,
        NetworkConfigurationValidationCode.WiredProfileTemplateMissing);
    public Visibility Dot1xProfileTemplateValidationVisibility => ToVisibility(Dot1xProfileTemplateValidationMessage);
    public string Dot1xCertificateValidationMessage => FormatFieldValidationMessage(
        NetworkConfigurationValidator.Validate(BuildDot1xOnlySettings(), CreateValidationInputs(), configurationStateService.IsMediaProtected),
        NetworkConfigurationValidationCode.WiredCertificateRequired,
        NetworkConfigurationValidationCode.WiredCertificateMissing);
    public Visibility Dot1xCertificateValidationVisibility => ToVisibility(Dot1xCertificateValidationMessage);
    public string WifiSsidValidationMessage => FormatFieldValidationMessage(
        NetworkConfigurationValidator.Validate(BuildWifiOnlySettings(), CreateValidationInputs(), configurationStateService.IsMediaProtected),
        NetworkConfigurationValidationCode.WifiSsidRequired);
    public Visibility WifiSsidValidationVisibility => ToVisibility(WifiSsidValidationMessage);
    public string WifiSecurityTypeValidationMessage => FormatFieldValidationMessage(
        NetworkConfigurationValidator.Validate(BuildWifiOnlySettings(), CreateValidationInputs(), configurationStateService.IsMediaProtected),
        NetworkConfigurationValidationCode.UnsupportedWifiSecurityType);
    public Visibility WifiSecurityTypeValidationVisibility => ToVisibility(WifiSecurityTypeValidationMessage);
    public string WifiPassphraseValidationMessage => FormatFieldValidationMessage(
        NetworkConfigurationValidator.Validate(BuildWifiOnlySettings(), CreateValidationInputs(), configurationStateService.IsMediaProtected),
        NetworkConfigurationValidationCode.WifiPersonalPassphraseInvalid);
    public Visibility WifiPassphraseValidationVisibility => ToVisibility(WifiPassphraseValidationMessage);
    public string WifiEnterpriseTemplateValidationMessage => FormatFieldValidationMessage(
        NetworkConfigurationValidator.Validate(BuildWifiOnlySettings(), CreateValidationInputs(), configurationStateService.IsMediaProtected),
        NetworkConfigurationValidationCode.WifiEnterpriseProfileTemplateRequired,
        NetworkConfigurationValidationCode.WifiEnterpriseProfileTemplateMissing,
        NetworkConfigurationValidationCode.WifiEnterpriseAuthenticationUnsupported,
        NetworkConfigurationValidationCode.WifiEnterpriseAuthenticationMismatch);
    public Visibility WifiEnterpriseTemplateValidationVisibility => ToVisibility(WifiEnterpriseTemplateValidationMessage);
    public string WifiCertificateValidationMessage => FormatFieldValidationMessage(
        NetworkConfigurationValidator.Validate(BuildWifiOnlySettings(), CreateValidationInputs(), configurationStateService.IsMediaProtected),
        NetworkConfigurationValidationCode.WifiEnterpriseCertificateRequired,
        NetworkConfigurationValidationCode.WifiEnterpriseCertificateMissing);
    public Visibility WifiCertificateValidationVisibility => ToVisibility(WifiCertificateValidationMessage);

    /// <summary>
    /// Releases subscriptions to localization and shared configuration state.
    /// </summary>
    public void Dispose()
    {
        localizationService.LanguageChanged -= OnLanguageChanged;
        configurationStateService.StateChanged -= OnConfigurationStateChanged;
    }

    [RelayCommand]
    private async Task BrowseDot1xProfileTemplateAsync()
    {
        if (await PickAndImportAsync("Network.ProfileTemplatePickerTitle", (string[])[".xml"], "WiredProfileTemplate") is { } reference)
        {
            dot1xProfileTemplate = reference;
            OnFileSelectionChanged();
        }
    }

    [RelayCommand]
    private async Task BrowseDot1xCertificateAsync()
    {
        if (await PickAndImportAsync("Dot1x.CertificatePickerTitle", (string[])[".cer", ".crt", ".pfx", "*"], "WiredCertificate") is { } reference)
        {
            SetDot1xCertificate(reference);
        }
    }

    [RelayCommand]
    private async Task BrowseWifiEnterpriseProfileTemplateAsync()
    {
        if (await PickAndImportAsync("Network.ProfileTemplatePickerTitle", (string[])[".xml"], "WifiProfileTemplate") is { } reference)
        {
            wifiEnterpriseProfileTemplate = reference;
            OnFileSelectionChanged();
        }
    }

    [RelayCommand]
    private async Task BrowseWifiCertificateAsync()
    {
        if (await PickAndImportAsync("Wifi.CertificatePickerTitle", (string[])[".cer", ".crt", ".pfx", "*"], "WifiCertificate") is { } reference)
        {
            SetWifiCertificate(reference);
        }
    }

    partial void OnIsDot1xEnabledChanged(bool value)
    {
        if (!value)
        {
            dot1xProfileTemplate = null;
            IsDot1xCertificateRequired = false;
            SetDot1xCertificate(null);
        }

        ClearWiredDot1xRoamingIfUnavailable();
        OnFileSelectionChanged();
    }

    partial void OnIsDot1xCertificateRequiredChanged(bool value)
    {
        if (!value)
        {
            SetDot1xCertificate(null);
        }

        SaveState();
        RefreshPresentationState();
    }

    partial void OnIsWifiProvisionedChanged(bool value)
    {
        if (!value)
        {
            IsWifiConfigured = false;
            networkSecretStateService.ClearPersonalWifiPassphrase();
        }

        ClearWifiRoamingIfUnavailable();
        SaveState();
        RefreshPresentationState();
    }

    partial void OnIsWifiConfiguredChanged(bool value)
    {
        if (!value)
        {
            WifiSsid = string.Empty;
            SelectedWifiSecurityType = SelectWifiSecurityOption(NetworkConfigurationValidator.WifiSecurityPersonal);
            WifiPassphrase = string.Empty;
            wifiEnterpriseProfileTemplate = null;
            IsWifiCertificateRequired = false;
            SetWifiCertificate(null);
            networkSecretStateService.ClearPersonalWifiPassphrase();
        }

        OnFileSelectionChanged();
    }

    partial void OnWifiSsidChanged(string value)
    {
        if (!isApplyingState)
        {
            WifiPassphrase = string.Empty;
            networkSecretStateService.ClearPersonalWifiPassphrase();
        }
        SaveState();
        RefreshPresentationState();
    }

    partial void OnSelectedWifiSecurityTypeChanged(SelectionOption<string>? value)
    {
        if (!IsWifiPersonalSelected)
        {
            WifiPassphrase = string.Empty;
            networkSecretStateService.ClearPersonalWifiPassphrase();
        }

        if (!IsWifiEnterpriseSelected)
        {
            wifiEnterpriseProfileTemplate = null;
            IsWifiCertificateRequired = false;
            SetWifiCertificate(null);
        }

        OnPropertyChanged(nameof(IsWifiPersonalSelected));
        OnPropertyChanged(nameof(IsWifiEnterpriseSelected));
        OnFileSelectionChanged();
    }

    partial void OnWifiPassphraseChanged(string value)
    {
        if (!isApplyingState && IsWifiPersonalSectionEnabled && string.IsNullOrWhiteSpace(value))
        {
            networkSecretStateService.ClearPersonalWifiPassphrase();
        }

        SaveState();
        RefreshPresentationState();
    }

    partial void OnIsWifiCertificateRequiredChanged(bool value)
    {
        if (!value)
        {
            SetWifiCertificate(null);
        }

        SaveState();
        RefreshPresentationState();
    }

    /// <summary>Selects a wired certificate; a different certificate never keeps the password typed for the previous one.</summary>
    private void SetDot1xCertificate(AuthoringFileReference? reference)
    {
        bool changed = reference != dot1xCertificate;
        dot1xCertificate = reference;
        if (changed && !isApplyingState)
        {
            Dot1xCertificatePassword = string.Empty;
        }

        OnFileSelectionChanged();
    }

    /// <summary>Selects a Wi-Fi certificate; a different certificate never keeps the password typed for the previous one.</summary>
    private void SetWifiCertificate(AuthoringFileReference? reference)
    {
        bool changed = reference != wifiCertificate;
        wifiCertificate = reference;
        if (changed && !isApplyingState)
        {
            WifiCertificatePassword = string.Empty;
        }

        OnFileSelectionChanged();
    }

    private void OnFileSelectionChanged()
    {
        RaiseFileSelectionChanged();
        SaveState();
        RefreshPresentationState();
    }

    private void RaiseFileSelectionChanged()
    {
        OnPropertyChanged(nameof(Dot1xProfileTemplateFileName));
        OnPropertyChanged(nameof(Dot1xCertificateFileName));
        OnPropertyChanged(nameof(WifiEnterpriseProfileTemplateFileName));
        OnPropertyChanged(nameof(WifiCertificateFileName));
        OnPropertyChanged(nameof(Dot1xValidationMessage));
        OnPropertyChanged(nameof(HasDot1xValidationError));
        OnPropertyChanged(nameof(Dot1xValidationVisibility));
        OnPropertyChanged(nameof(WifiValidationMessage));
        OnPropertyChanged(nameof(HasWifiValidationError));
        OnPropertyChanged(nameof(WifiValidationVisibility));
        OnPropertyChanged(nameof(ValidationMessage));
        OnPropertyChanged(nameof(HasValidationError));
    }

    /// <summary>Imports a picked file into the authoring file library; a failure is logged and shown, and returns null.</summary>
    private async Task<AuthoringFileReference?> PickAndImportAsync(string titleKey, IReadOnlyList<string> filters, string fileKind)
    {
        string? path = await PickOpenFileAsync(titleKey, filters);
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            AuthoringFileReference reference = await authoringFiles.ImportAsync(path);
            logger.Information("Network file selected. FileKind={FileKind}, ContentHash={ContentHash}", fileKind, reference.ContentHash);
            return reference;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or NotSupportedException)
        {
            logger.Warning(exception, "A selected network file could not be imported into the authoring file library. FileKind={FileKind}", fileKind);
            await dialogService.ShowMessageAsync(new DialogRequest(
                localizationService.GetString("AuthoringFiles.ImportFailedTitle"),
                localizationService.GetString("AuthoringFiles.ImportFailedMessage"),
                localizationService.GetString("Common.Close")));
            return null;
        }
    }

    partial void OnIsWiredDot1xProfileRoamingEnabledChanged(bool value)
    {
        if (!isApplyingState && value && !IsWiredDot1xProfileRoamingAvailable)
        {
            IsWiredDot1xProfileRoamingEnabled = false;
            return;
        }

        if (!value)
        {
            IsWiredDot1xPrivateKeyMaterialRoamingRequested = false;
        }

        SaveState();
        RefreshPresentationState();
    }

    partial void OnIsWiredDot1xPrivateKeyMaterialRoamingRequestedChanged(bool value)
    {
        SaveState();
        RefreshPresentationState();
    }

    partial void OnIsWifiProfileRoamingEnabledChanged(bool value)
    {
        if (!isApplyingState && value && !IsWifiProfileRoamingAvailable)
        {
            IsWifiProfileRoamingEnabled = false;
            return;
        }

        if (!value)
        {
            IsWifiPrivateKeyMaterialRoamingRequested = false;
        }

        SaveState();
        RefreshPresentationState();
    }

    partial void OnIsWifiPrivateKeyMaterialRoamingRequestedChanged(bool value)
    {
        SaveState();
        RefreshPresentationState();
    }

    private async Task<string?> PickOpenFileAsync(string titleKey, IReadOnlyList<string> filters)
    {
        return await filePickerService.PickOpenFileAsync(
            new FileOpenPickerRequest(localizationService.GetString(titleKey), filters));
    }

    private void ApplyState(NetworkSettings settings)
    {
        isApplyingState = true;
        NetworkSecrets secrets = networkSecretStateService.GetSecrets(settings);
        IsDot1xEnabled = settings.Dot1x.IsEnabled;
        dot1xProfileTemplate = settings.Dot1x.ProfileTemplate;
        IsDot1xCertificateRequired = settings.Dot1x.RequiresCertificate;
        dot1xCertificate = settings.Dot1x.ClientCertificate ?? settings.Dot1x.TrustedRootCertificates.FirstOrDefault();
        Dot1xCertificatePassword = secrets.WiredCertificatePfxPassword ?? string.Empty;

        IsWifiProvisioned = settings.WifiProvisioned || settings.Wifi.IsEnabled;
        IsWifiConfigured = settings.Wifi.IsEnabled;
        WifiSsid = settings.Wifi.Ssid ?? string.Empty;
        SelectedWifiSecurityType = SelectWifiSecurityOption(NetworkConfigurationValidator.NormalizeWifiSecurityType(settings.Wifi));
        WifiPassphrase = ResolveWifiPassphrase(settings);
        wifiEnterpriseProfileTemplate = settings.Wifi.EnterpriseProfileTemplate;
        IsWifiCertificateRequired = settings.Wifi.RequiresCertificate;
        wifiCertificate = settings.Wifi.ClientCertificate ?? settings.Wifi.TrustedRootCertificates.FirstOrDefault();
        WifiCertificatePassword = secrets.WifiCertificatePfxPassword ?? string.Empty;
        IsWiredDot1xProfileRoamingEnabled = settings.RoamWiredDot1xProfileToWindows;
        IsWiredDot1xPrivateKeyMaterialRoamingRequested = settings.RoamWiredDot1xPrivateKeyMaterialToWindows;
        IsWifiProfileRoamingEnabled = settings.RoamWifiProfileToWindows;
        IsWifiPrivateKeyMaterialRoamingRequested = settings.RoamWifiPrivateKeyMaterialToWindows;
        isApplyingState = false;
        RaiseFileSelectionChanged();
        RefreshPresentationState();
    }

    private void SaveState()
    {
        if (isApplyingState)
        {
            return;
        }

        isSavingState = true;
        try
        {
            // A managed configuration is read-only: only the passwords the publication does not include are entered here.
            if (configurationStateService.IsManaged)
            {
                networkSecretStateService.Update(configurationStateService.Current.Network, BuildEnteredSecrets());
                return;
            }

            // Store only the network model; passwords stay in the secret state service.
            NetworkSettings settings = BuildSettingsForValidation();
            networkSecretStateService.Update(settings, BuildEnteredSecrets());
            configurationStateService.UpdateNetwork(settings);
        }
        finally
        {
            isSavingState = false;
        }
    }

    private NetworkSecrets BuildEnteredSecrets()
    {
        return new NetworkSecrets
        {
            WifiPassphrase = IsWifiPersonalSectionEnabled && !string.IsNullOrWhiteSpace(WifiPassphrase)
                ? WifiPassphrase.Trim()
                : null,
            WiredCertificatePfxPassword = IsDot1xCertificatePathEnabled ? Dot1xCertificatePassword : null,
            WifiCertificatePfxPassword = IsWifiCertificatePathEnabled ? WifiCertificatePassword : null
        };
    }

    private MediaInputs CreateValidationInputs()
    {
        return new MediaInputs
        {
            ResolveFile = authoringFiles.GetAvailablePath,
            Network = BuildEnteredSecrets()
        };
    }

    private NetworkSettings BuildSettingsForValidation()
    {
        return new NetworkSettings
        {
            RoamWiredDot1xProfileToWindows = IsWiredDot1xProfileRoamingEnabled,
            RoamWiredDot1xPrivateKeyMaterialToWindows = IsWiredDot1xPrivateKeyMaterialRoamingRequested,
            RoamWifiProfileToWindows = IsWifiProfileRoamingEnabled,
            RoamWifiPrivateKeyMaterialToWindows = IsWifiPrivateKeyMaterialRoamingRequested,
            Dot1x = NetworkCertificateShapes.WithCertificate(new Dot1xSettings
            {
                IsEnabled = IsDot1xEnabled,
                ProfileTemplate = IsDot1xEnabled ? dot1xProfileTemplate : null,
                AuthenticationMode = NetworkAuthenticationMode.MachineOnly,
                AllowRuntimeCredentials = false,
                RequiresCertificate = IsDot1xEnabled && IsDot1xCertificateRequired
            }, IsDot1xCertificatePathEnabled ? dot1xCertificate : null),
            WifiProvisioned = IsWifiProvisioned,
            Wifi = NetworkCertificateShapes.WithCertificate(new WifiSettings
            {
                IsEnabled = IsWifiConfigured,
                Ssid = IsWifiConfigured && !string.IsNullOrWhiteSpace(WifiSsid)
                    ? WifiSsid.Trim()
                    : null,
                SecurityType = IsWifiConfigured && !string.IsNullOrWhiteSpace(SelectedWifiSecurityType?.Value)
                    ? SelectedWifiSecurityType.Value.Trim()
                    : null,
                HasEnterpriseProfile = IsWifiEnterpriseSectionEnabled,
                EnterpriseProfileTemplate = IsWifiEnterpriseSectionEnabled ? wifiEnterpriseProfileTemplate : null,
                EnterpriseAuthenticationMode = NetworkAuthenticationMode.UserOnly,
                AllowRuntimeCredentials = false,
                RequiresCertificate = IsWifiEnterpriseSectionEnabled && IsWifiCertificateRequired
            }, IsWifiCertificatePathEnabled ? wifiCertificate : null)
        };
    }

    private NetworkSettings BuildDot1xOnlySettings()
    {
        return BuildSettingsForValidation() with
        {
            WifiProvisioned = false,
            Wifi = new WifiSettings()
        };
    }

    private NetworkSettings BuildWifiOnlySettings()
    {
        return BuildSettingsForValidation() with
        {
            Dot1x = new Dot1xSettings()
        };
    }

    private void RefreshLocalizedText()
    {
        ProfileTemplateLabel = localizationService.GetString("Network.ProfileTemplateLabel");
        Dot1xCertificateLabel = localizationService.GetString("Dot1x.CertificateLabel");
        CertificatePasswordLabel = localizationService.GetString("Autopilot.HardwareHashBootMediaCertificatePasswordLabel");
        RequiresCertificateText = localizationService.GetString("Network.RequiresCertificateLabel");
        WifiConfiguredText = localizationService.GetString("Wifi.ConfigureLabel");
        WifiSsidLabel = localizationService.GetString("Wifi.SsidLabel");
        WifiSecurityTypeLabel = localizationService.GetString("Wifi.SecurityTypeLabel");
        WifiPassphraseLabel = localizationService.GetString("Wifi.PassphraseLabel");
        WifiPersonalDescription = localizationService.GetString("Network.WifiPersonalHelperText");
        WifiEnterpriseHeader = localizationService.GetString("Network.WifiEnterpriseAdvancedHeader");
        WifiEnterpriseDescription = localizationService.GetString("Network.EnterpriseTemplateDrivenHelperText");
        WifiEnterpriseTemplateText = localizationService.GetString("Network.WifiEnterpriseTemplateLabel");
        WifiCertificateLabel = localizationService.GetString("Wifi.CertificateLabel");
        WiredDot1xWindowsRoamingHeader = localizationService.GetString("Network.WiredDot1xWindowsRoamingSectionTitle");
        WiredDot1xWindowsRoamingDescription = localizationService.GetString("Network.WiredDot1xWindowsRoamingHelperText");
        WifiWindowsRoamingHeader = localizationService.GetString("Network.WifiWindowsRoamingSectionTitle");
        WifiWindowsRoamingDescription = localizationService.GetString("Network.WifiWindowsRoamingHelperText");
        RoamNetworkProfilesText = localizationService.GetString("Network.RoamProfilesLabel");
        RoamPrivateKeyMaterialText = localizationService.GetString("Network.RoamPrivateKeyMaterialLabel");
        RoamPrivateKeyMaterialDescription = localizationService.GetString("Network.RoamPrivateKeyMaterialHelperText");
        BrowseButtonText = localizationService.GetString("Common.Browse");
        RefreshPresentationState();
    }

    private void RefreshWifiSecurityTypes()
    {
        string? selectedValue = SelectedWifiSecurityType?.Value ?? NetworkConfigurationValidator.WifiSecurityPersonal;
        WifiSecurityTypes.Clear();
        WifiSecurityTypes.Add(new(NetworkConfigurationValidator.WifiSecurityOpen, localizationService.GetString("Wifi.SecurityTypeOpen")));
        WifiSecurityTypes.Add(new(NetworkConfigurationValidator.WifiSecurityOwe, localizationService.GetString("Wifi.SecurityTypeOwe")));
        WifiSecurityTypes.Add(new(NetworkConfigurationValidator.WifiSecurityPersonal, localizationService.GetString("Wifi.SecurityTypePersonal")));
        WifiSecurityTypes.Add(new(NetworkConfigurationValidator.WifiSecurityEnterprise, localizationService.GetString("Wifi.SecurityTypeEnterprise")));
        WifiSecurityTypes.Add(new(NetworkConfigurationValidator.WifiSecurityEnterpriseWpa3, localizationService.GetString("Wifi.SecurityTypeEnterpriseWpa3")));
        WifiSecurityTypes.Add(new(NetworkConfigurationValidator.WifiSecurityEnterpriseWpa3192, localizationService.GetString("Wifi.SecurityTypeEnterpriseWpa3192")));
        SelectedWifiSecurityType = SelectWifiSecurityOption(selectedValue);
    }

    private SelectionOption<string> SelectWifiSecurityOption(string? value)
    {
        return WifiSecurityTypes.FirstOrDefault(option => string.Equals(option.Value, value, StringComparison.OrdinalIgnoreCase))
            ?? WifiSecurityTypes.First(option => string.Equals(option.Value, NetworkConfigurationValidator.WifiSecurityPersonal, StringComparison.OrdinalIgnoreCase));
    }

    private void OnLanguageChanged(object? sender, ApplicationLanguageChangedEventArgs e)
    {
        RefreshLocalizedText();
        RefreshWifiSecurityTypes();
    }

    private void OnConfigurationStateChanged(object? sender, EventArgs e)
    {
        if (isSavingState)
        {
            return;
        }

        ApplyState(configurationStateService.Current.Network);
    }

    private void RefreshPresentationState()
    {
        OnPropertyChanged(nameof(Dot1xCertificatePathVisibility));
        OnPropertyChanged(nameof(WifiConfigurationFieldsVisibility));
        OnPropertyChanged(nameof(WifiPersonalSettingsVisibility));
        OnPropertyChanged(nameof(WifiEnterpriseSettingsVisibility));
        OnPropertyChanged(nameof(WifiCertificatePathVisibility));
        OnPropertyChanged(nameof(IsWiredDot1xProfileRoamingAvailable));
        OnPropertyChanged(nameof(IsWifiProfileRoamingAvailable));
        OnPropertyChanged(nameof(IsWiredDot1xPrivateKeyMaterialRoamingEnabled));
        OnPropertyChanged(nameof(IsWifiPrivateKeyMaterialRoamingEnabled));
        OnPropertyChanged(nameof(WiredDot1xRoamPrivateKeyMaterialVisibility));
        OnPropertyChanged(nameof(WifiRoamPrivateKeyMaterialVisibility));
        OnPropertyChanged(nameof(Dot1xProfileTemplateValidationMessage));
        OnPropertyChanged(nameof(Dot1xProfileTemplateValidationVisibility));
        OnPropertyChanged(nameof(Dot1xCertificateValidationMessage));
        OnPropertyChanged(nameof(Dot1xCertificateValidationVisibility));
        OnPropertyChanged(nameof(WifiSsidValidationMessage));
        OnPropertyChanged(nameof(WifiSsidValidationVisibility));
        OnPropertyChanged(nameof(WifiSecurityTypeValidationMessage));
        OnPropertyChanged(nameof(WifiSecurityTypeValidationVisibility));
        OnPropertyChanged(nameof(WifiPassphraseValidationMessage));
        OnPropertyChanged(nameof(WifiPassphraseValidationVisibility));
        OnPropertyChanged(nameof(WifiEnterpriseTemplateValidationMessage));
        OnPropertyChanged(nameof(WifiEnterpriseTemplateValidationVisibility));
        OnPropertyChanged(nameof(WifiCertificateValidationMessage));
        OnPropertyChanged(nameof(WifiCertificateValidationVisibility));

        IsWiredDot1xWindowsRoamingExpanded = IsWiredDot1xProfileRoamingAvailable && IsWiredDot1xProfileRoamingEnabled;
        IsWifiWindowsRoamingExpanded = IsWifiProfileRoamingAvailable && IsWifiProfileRoamingEnabled;
    }

    private void ClearWiredDot1xRoamingIfUnavailable()
    {
        if (IsWiredDot1xProfileRoamingAvailable)
        {
            return;
        }

        IsWiredDot1xProfileRoamingEnabled = false;
        IsWiredDot1xPrivateKeyMaterialRoamingRequested = false;
        IsWiredDot1xWindowsRoamingExpanded = false;
    }

    private void ClearWifiRoamingIfUnavailable()
    {
        if (IsWifiProfileRoamingAvailable)
        {
            return;
        }

        IsWifiProfileRoamingEnabled = false;
        IsWifiPrivateKeyMaterialRoamingRequested = false;
        IsWifiWindowsRoamingExpanded = false;
    }

    private string ResolveWifiPassphrase(NetworkSettings settings)
    {
        if (!(settings.WifiProvisioned || settings.Wifi.IsEnabled) ||
            !settings.Wifi.IsEnabled ||
            !IsWifiPersonalSelected)
        {
            return string.Empty;
        }

        return networkSecretStateService.PersonalWifiPassphrase ?? string.Empty;
    }

    private string FormatFieldValidationMessage(
        NetworkConfigurationValidationResult result,
        params NetworkConfigurationValidationCode[] supportedCodes)
    {
        return supportedCodes.Contains(result.Code)
            ? FormatValidationMessage(result, IsDot1xValidationCode(result.Code))
            : string.Empty;
    }

    private static Visibility ToVisibility(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? Visibility.Collapsed : Visibility.Visible;
    }

    private static bool IsDot1xValidationCode(NetworkConfigurationValidationCode code)
    {
        return code is
            NetworkConfigurationValidationCode.WiredProfileTemplateRequired or
            NetworkConfigurationValidationCode.WiredProfileTemplateMissing or
            NetworkConfigurationValidationCode.WiredCertificateRequired or
            NetworkConfigurationValidationCode.WiredCertificateMissing;
    }

    private string FormatValidationMessage(NetworkConfigurationValidationResult result, bool dot1xOnly)
    {
        if (result.IsValid)
        {
            return string.Empty;
        }

        if (dot1xOnly && result.Code is not (
            NetworkConfigurationValidationCode.WiredProfileTemplateRequired or
            NetworkConfigurationValidationCode.WiredProfileTemplateMissing or
            NetworkConfigurationValidationCode.WiredCertificateRequired or
            NetworkConfigurationValidationCode.WiredCertificateMissing))
        {
            return string.Empty;
        }

        if (!dot1xOnly && result.Code is
            NetworkConfigurationValidationCode.WiredProfileTemplateRequired or
            NetworkConfigurationValidationCode.WiredProfileTemplateMissing or
            NetworkConfigurationValidationCode.WiredCertificateRequired or
            NetworkConfigurationValidationCode.WiredCertificateMissing)
        {
            return string.Empty;
        }

        return NetworkConfigurationValidationTextFormatter.Format(localizationService, result);
    }
}
