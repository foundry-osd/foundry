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
/// Each transport has two certificate slots: one client certificate (PFX) and a list of trusted root CA certificates; both are
/// saved on every edit. Passphrases and PFX passwords are routed through <see cref="INetworkSecretStateService"/>, the PFX
/// password bound to the client certificate content hash, so they can be used for provisioning without entering the configuration.
/// </remarks>
public sealed partial class NetworkConfigurationViewModel : ObservableObject, IDisposable
{
    private static readonly string[] ClientCertificateFilters = [".pfx", ".p12"];
    private static readonly string[] TrustedRootCertificateFilters = [".cer", ".crt", "*"];

    private readonly IFoundryConfigurationStateService configurationStateService;
    private readonly INetworkSecretStateService networkSecretStateService;
    private readonly IDeploymentProtectionSecretStateService deploymentProtectionSecretStateService;
    private readonly IFilePickerService filePickerService;
    private readonly IDialogService dialogService;
    private readonly AuthoringFileLibrary authoringFiles;
    private readonly IApplicationLocalizationService localizationService;
    private readonly ILogger logger;
    private bool isApplyingState = true;
    private bool isSavingState;
    private AuthoringFileReference? dot1xProfileTemplate;
    private AuthoringFileReference? dot1xClientCertificate;
    private AuthoringFileReference? wifiEnterpriseProfileTemplate;
    private AuthoringFileReference? wifiClientCertificate;

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
        IDeploymentProtectionSecretStateService deploymentProtectionSecretStateService,
        IFilePickerService filePickerService,
        IDialogService dialogService,
        AuthoringFileLibrary authoringFiles,
        IApplicationLocalizationService localizationService,
        ILogger logger)
    {
        this.configurationStateService = configurationStateService;
        this.networkSecretStateService = networkSecretStateService;
        this.deploymentProtectionSecretStateService = deploymentProtectionSecretStateService;
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
        deploymentProtectionSecretStateService.Changed += OnDeploymentProtectionChanged;
        isApplyingState = false;
    }

    /// <summary>
    /// Gets the Wi-Fi security type options shown by the network page.
    /// </summary>
    public ObservableCollection<SelectionOption<string>> WifiSecurityTypes { get; } = [];

    /// <summary>Gets the wired 802.1X trusted root CA certificates, in the order they are staged.</summary>
    public ObservableCollection<TrustedRootCertificateEntry> Dot1xTrustedRootCertificates { get; } = [];

    /// <summary>Gets the enterprise Wi-Fi trusted root CA certificates, in the order they are staged.</summary>
    public ObservableCollection<TrustedRootCertificateEntry> WifiTrustedRootCertificates { get; } = [];

    /// <summary>Gets or sets the wired trusted root certificate selected in the list; Remove acts on it.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveDot1xTrustedRootCertificateCommand))]
    public partial TrustedRootCertificateEntry? SelectedDot1xTrustedRootCertificate { get; set; }

    /// <summary>Gets or sets the Wi-Fi trusted root certificate selected in the list; Remove acts on it.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveWifiTrustedRootCertificateCommand))]
    public partial TrustedRootCertificateEntry? SelectedWifiTrustedRootCertificate { get; set; }

    [ObservableProperty]
    public partial string ProfileTemplateLabel { get; set; }

    /// <summary>Gets the header of the client certificate (PFX) card.</summary>
    [ObservableProperty]
    public partial string ClientCertificateLabel { get; set; }

    /// <summary>Gets the description of the client certificate card.</summary>
    [ObservableProperty]
    public partial string ClientCertificateDescription { get; set; }

    /// <summary>Gets the header of the trusted root CA certificates card.</summary>
    [ObservableProperty]
    public partial string TrustedRootCertificatesLabel { get; set; }

    /// <summary>Gets the description of the trusted root CA certificates card.</summary>
    [ObservableProperty]
    public partial string TrustedRootCertificatesDescription { get; set; }

    /// <summary>Gets the text of the button that adds a trusted root certificate.</summary>
    [ObservableProperty]
    public partial string AddTrustedRootCertificateText { get; set; }

    /// <summary>Gets the visible text of the two Remove buttons.</summary>
    [ObservableProperty]
    public partial string RemoveCertificateText { get; set; }

    /// <summary>Gets the accessible name of the button that removes the client certificate.</summary>
    [ObservableProperty]
    public partial string RemoveClientCertificateText { get; set; }

    /// <summary>Gets the accessible name of the button that removes the selected trusted root certificate.</summary>
    [ObservableProperty]
    public partial string RemoveTrustedRootCertificateText { get; set; }

    /// <summary>Gets the text shown when no trusted root certificate is listed.</summary>
    [ObservableProperty]
    public partial string NoTrustedRootCertificatesText { get; set; }

    /// <summary>Gets the title of the Password protection InfoBar.</summary>
    [ObservableProperty]
    public partial string NeedsAttentionText { get; set; }

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

    /// <summary>Gets the file name of the selected wired client certificate.</summary>
    public string Dot1xClientCertificateFileName => dot1xClientCertificate?.FileName ?? string.Empty;

    /// <summary>Gets the file name of the selected enterprise Wi-Fi profile template.</summary>
    public string WifiEnterpriseProfileTemplateFileName => wifiEnterpriseProfileTemplate?.FileName ?? string.Empty;

    /// <summary>Gets the file name of the selected Wi-Fi client certificate.</summary>
    public string WifiClientCertificateFileName => wifiClientCertificate?.FileName ?? string.Empty;

    /// <summary>Gets whether the PFX password box is shown: only a client certificate has a password.</summary>
    public Visibility Dot1xClientCertificatePasswordVisibility => dot1xClientCertificate is null ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Gets whether the PFX password box is shown: only a client certificate has a password.</summary>
    public Visibility WifiClientCertificatePasswordVisibility => wifiClientCertificate is null ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Gets whether the empty-list text of the wired trusted roots is shown.</summary>
    public Visibility Dot1xNoTrustedRootCertificatesVisibility => Dot1xTrustedRootCertificates.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Gets whether the empty-list text of the Wi-Fi trusted roots is shown.</summary>
    public Visibility WifiNoTrustedRootCertificatesVisibility => WifiTrustedRootCertificates.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

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

    /// <summary>Gets the "Needs attention" text shown when a client certificate is provisioned without Password protection.</summary>
    public string Dot1xMediaProtectionMessage => FormatFieldValidationMessage(
        NetworkConfigurationValidator.Validate(BuildDot1xOnlySettings(), CreateValidationInputs(), configurationStateService.IsMediaProtected),
        NetworkConfigurationValidationCode.WiredMediaProtectionRequired);

    /// <summary>Gets whether the wired Password protection InfoBar is open.</summary>
    public bool HasDot1xMediaProtectionIssue => !string.IsNullOrWhiteSpace(Dot1xMediaProtectionMessage);

    /// <summary>Gets the "Needs attention" text shown when a passphrase or a client certificate is provisioned without Password protection.</summary>
    public string WifiMediaProtectionMessage => FormatFieldValidationMessage(
        NetworkConfigurationValidator.Validate(BuildWifiOnlySettings(), CreateValidationInputs(), configurationStateService.IsMediaProtected),
        NetworkConfigurationValidationCode.WifiMediaProtectionRequired);

    /// <summary>Gets whether the Wi-Fi Password protection InfoBar is open.</summary>
    public bool HasWifiMediaProtectionIssue => !string.IsNullOrWhiteSpace(WifiMediaProtectionMessage);

    /// <summary>
    /// Releases subscriptions to localization and shared configuration state.
    /// </summary>
    public void Dispose()
    {
        localizationService.LanguageChanged -= OnLanguageChanged;
        configurationStateService.StateChanged -= OnConfigurationStateChanged;
        deploymentProtectionSecretStateService.Changed -= OnDeploymentProtectionChanged;
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
    private async Task BrowseDot1xClientCertificateAsync()
    {
        if (await PickAndImportAsync("Network.ClientCertificatePickerTitle", ClientCertificateFilters, "WiredClientCertificate") is { } reference)
        {
            SetDot1xClientCertificate(reference);
        }
    }

    [RelayCommand(CanExecute = nameof(CanRemoveDot1xClientCertificate))]
    private void RemoveDot1xClientCertificate() => SetDot1xClientCertificate(null);

    private bool CanRemoveDot1xClientCertificate() => dot1xClientCertificate is not null;

    [RelayCommand]
    private async Task AddDot1xTrustedRootCertificateAsync()
    {
        if (await PickAndImportAsync("Network.TrustedRootCertificatePickerTitle", TrustedRootCertificateFilters, "WiredTrustedRootCertificate") is { } reference &&
            TryAddTrustedRoot(Dot1xTrustedRootCertificates, reference))
        {
            OnFileSelectionChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanRemoveDot1xTrustedRootCertificate))]
    private void RemoveDot1xTrustedRootCertificate()
    {
        if (SelectedDot1xTrustedRootCertificate is not { } selected)
        {
            return;
        }

        SelectedDot1xTrustedRootCertificate = null;
        Dot1xTrustedRootCertificates.Remove(selected);
        OnFileSelectionChanged();
    }

    private bool CanRemoveDot1xTrustedRootCertificate() => SelectedDot1xTrustedRootCertificate is not null;

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
    private async Task BrowseWifiClientCertificateAsync()
    {
        if (await PickAndImportAsync("Network.ClientCertificatePickerTitle", ClientCertificateFilters, "WifiClientCertificate") is { } reference)
        {
            SetWifiClientCertificate(reference);
        }
    }

    [RelayCommand(CanExecute = nameof(CanRemoveWifiClientCertificate))]
    private void RemoveWifiClientCertificate() => SetWifiClientCertificate(null);

    private bool CanRemoveWifiClientCertificate() => wifiClientCertificate is not null;

    [RelayCommand]
    private async Task AddWifiTrustedRootCertificateAsync()
    {
        if (await PickAndImportAsync("Network.TrustedRootCertificatePickerTitle", TrustedRootCertificateFilters, "WifiTrustedRootCertificate") is { } reference &&
            TryAddTrustedRoot(WifiTrustedRootCertificates, reference))
        {
            OnFileSelectionChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanRemoveWifiTrustedRootCertificate))]
    private void RemoveWifiTrustedRootCertificate()
    {
        if (SelectedWifiTrustedRootCertificate is not { } selected)
        {
            return;
        }

        SelectedWifiTrustedRootCertificate = null;
        WifiTrustedRootCertificates.Remove(selected);
        OnFileSelectionChanged();
    }

    private bool CanRemoveWifiTrustedRootCertificate() => SelectedWifiTrustedRootCertificate is not null;

    partial void OnIsDot1xEnabledChanged(bool value)
    {
        if (!value)
        {
            dot1xProfileTemplate = null;
            IsDot1xCertificateRequired = false;
            ClearDot1xCertificates();
        }

        ClearWiredDot1xRoamingIfUnavailable();
        OnFileSelectionChanged();
    }

    partial void OnIsDot1xCertificateRequiredChanged(bool value)
    {
        if (!value)
        {
            ClearDot1xCertificates();
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
            ClearWifiCertificates();
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
            ClearWifiCertificates();
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
            ClearWifiCertificates();
        }

        SaveState();
        RefreshPresentationState();
    }

    /// <summary>Selects the wired client certificate; a different certificate never keeps the password typed for the previous one.</summary>
    private void SetDot1xClientCertificate(AuthoringFileReference? reference)
    {
        bool changed = reference != dot1xClientCertificate;
        dot1xClientCertificate = reference;
        if (changed && !isApplyingState)
        {
            Dot1xCertificatePassword = string.Empty;
        }

        OnFileSelectionChanged();
    }

    /// <summary>Selects the Wi-Fi client certificate; a different certificate never keeps the password typed for the previous one.</summary>
    private void SetWifiClientCertificate(AuthoringFileReference? reference)
    {
        bool changed = reference != wifiClientCertificate;
        wifiClientCertificate = reference;
        if (changed && !isApplyingState)
        {
            WifiCertificatePassword = string.Empty;
        }

        OnFileSelectionChanged();
    }

    /// <summary>Drops both wired certificate slots when certificates are no longer used.</summary>
    private void ClearDot1xCertificates()
    {
        SelectedDot1xTrustedRootCertificate = null;
        Dot1xTrustedRootCertificates.Clear();
        SetDot1xClientCertificate(null);
    }

    /// <summary>Drops both Wi-Fi certificate slots when certificates are no longer used.</summary>
    private void ClearWifiCertificates()
    {
        SelectedWifiTrustedRootCertificate = null;
        WifiTrustedRootCertificates.Clear();
        SetWifiClientCertificate(null);
    }

    /// <summary>Appends a trusted root unless a certificate with the same content hash is already listed.</summary>
    private static bool TryAddTrustedRoot(ObservableCollection<TrustedRootCertificateEntry> entries, AuthoringFileReference reference)
    {
        if (entries.Any(entry => IsSameFile(entry.Reference, reference)))
        {
            return false;
        }

        entries.Add(new TrustedRootCertificateEntry(reference));
        return true;
    }

    /// <summary>
    /// Mirrors the persisted trusted roots; the list is rebuilt only when its content hashes changed, so a state echo keeps the
    /// selection, and a rebuilt list keeps the previously selected certificate when it is still there.
    /// </summary>
    private static TrustedRootCertificateEntry? SyncTrustedRoots(
        ObservableCollection<TrustedRootCertificateEntry> entries,
        IReadOnlyList<AuthoringFileReference> roots,
        TrustedRootCertificateEntry? selected)
    {
        if (entries.Select(entry => entry.Reference.ContentHash).SequenceEqual(roots.Select(root => root.ContentHash), StringComparer.OrdinalIgnoreCase))
        {
            return selected;
        }

        entries.Clear();
        foreach (AuthoringFileReference root in roots)
        {
            entries.Add(new TrustedRootCertificateEntry(root));
        }

        return selected is null ? null : entries.FirstOrDefault(entry => IsSameFile(entry.Reference, selected.Reference));
    }

    private static bool IsSameFile(AuthoringFileReference left, AuthoringFileReference right) =>
        string.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase);

    private void OnFileSelectionChanged()
    {
        RaiseFileSelectionChanged();
        SaveState();
        RefreshPresentationState();
    }

    private void RaiseFileSelectionChanged()
    {
        OnPropertyChanged(nameof(Dot1xProfileTemplateFileName));
        OnPropertyChanged(nameof(Dot1xClientCertificateFileName));
        OnPropertyChanged(nameof(Dot1xClientCertificatePasswordVisibility));
        OnPropertyChanged(nameof(Dot1xNoTrustedRootCertificatesVisibility));
        OnPropertyChanged(nameof(WifiEnterpriseProfileTemplateFileName));
        OnPropertyChanged(nameof(WifiClientCertificateFileName));
        OnPropertyChanged(nameof(WifiClientCertificatePasswordVisibility));
        OnPropertyChanged(nameof(WifiNoTrustedRootCertificatesVisibility));
        RemoveDot1xClientCertificateCommand.NotifyCanExecuteChanged();
        RemoveWifiClientCertificateCommand.NotifyCanExecuteChanged();
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
            // No exception object: its message names the picked file.
            logger.Warning(
                "A selected network file could not be imported into the authoring file library. FileKind={FileKind}, FailureType={FailureType}",
                fileKind,
                exception.GetType().Name);
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
        dot1xClientCertificate = settings.Dot1x.ClientCertificate;
        SelectedDot1xTrustedRootCertificate = SyncTrustedRoots(
            Dot1xTrustedRootCertificates,
            settings.Dot1x.TrustedRootCertificates,
            SelectedDot1xTrustedRootCertificate);
        Dot1xCertificatePassword = secrets.WiredCertificatePfxPassword ?? string.Empty;

        IsWifiProvisioned = settings.WifiProvisioned || settings.Wifi.IsEnabled;
        IsWifiConfigured = settings.Wifi.IsEnabled;
        WifiSsid = settings.Wifi.Ssid ?? string.Empty;
        SelectedWifiSecurityType = SelectWifiSecurityOption(NetworkConfigurationValidator.NormalizeWifiSecurityType(settings.Wifi));
        WifiPassphrase = ResolveWifiPassphrase(settings);
        wifiEnterpriseProfileTemplate = settings.Wifi.EnterpriseProfileTemplate;
        IsWifiCertificateRequired = settings.Wifi.RequiresCertificate;
        wifiClientCertificate = settings.Wifi.ClientCertificate;
        SelectedWifiTrustedRootCertificate = SyncTrustedRoots(
            WifiTrustedRootCertificates,
            settings.Wifi.TrustedRootCertificates,
            SelectedWifiTrustedRootCertificate);
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
            WiredCertificatePfxPassword = IsDot1xCertificatePathEnabled && dot1xClientCertificate is not null ? Dot1xCertificatePassword : null,
            WifiCertificatePfxPassword = IsWifiCertificatePathEnabled && wifiClientCertificate is not null ? WifiCertificatePassword : null
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
            Dot1x = new Dot1xSettings
            {
                IsEnabled = IsDot1xEnabled,
                ProfileTemplate = IsDot1xEnabled ? dot1xProfileTemplate : null,
                AuthenticationMode = NetworkAuthenticationMode.MachineOnly,
                AllowRuntimeCredentials = false,
                RequiresCertificate = IsDot1xEnabled && IsDot1xCertificateRequired,
                ClientCertificate = IsDot1xCertificatePathEnabled ? dot1xClientCertificate : null,
                TrustedRootCertificates = IsDot1xCertificatePathEnabled ? ToReferences(Dot1xTrustedRootCertificates) : []
            },
            WifiProvisioned = IsWifiProvisioned,
            Wifi = new WifiSettings
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
                RequiresCertificate = IsWifiEnterpriseSectionEnabled && IsWifiCertificateRequired,
                ClientCertificate = IsWifiCertificatePathEnabled ? wifiClientCertificate : null,
                TrustedRootCertificates = IsWifiCertificatePathEnabled ? ToReferences(WifiTrustedRootCertificates) : []
            }
        };
    }

    private static AuthoringFileReference[] ToReferences(IEnumerable<TrustedRootCertificateEntry> entries) =>
        [.. entries.Select(entry => entry.Reference)];

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
        ClientCertificateLabel = localizationService.GetString("Network.ClientCertificateLabel");
        ClientCertificateDescription = localizationService.GetString("Network.ClientCertificateDescription");
        TrustedRootCertificatesLabel = localizationService.GetString("Network.TrustedRootCertificatesLabel");
        TrustedRootCertificatesDescription = localizationService.GetString("Network.TrustedRootCertificatesDescription");
        AddTrustedRootCertificateText = localizationService.GetString("Network.AddTrustedRootCertificate");
        RemoveCertificateText = localizationService.GetString("Network.RemoveCertificate");
        RemoveClientCertificateText = localizationService.GetString("Network.RemoveClientCertificate");
        RemoveTrustedRootCertificateText = localizationService.GetString("Network.RemoveTrustedRootCertificate");
        NoTrustedRootCertificatesText = localizationService.GetString("Network.NoTrustedRootCertificates");
        NeedsAttentionText = localizationService.GetString("Common.NeedsAttention");
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

    /// <summary>
    /// The deployment password changes on the General page without a configuration change; network secrets require it
    /// (<see cref="IFoundryConfigurationStateService.IsMediaProtected"/>), so the validation texts are re-read.
    /// </summary>
    private void OnDeploymentProtectionChanged(object? sender, EventArgs e)
    {
        RaiseFileSelectionChanged();
        RefreshPresentationState();
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
        OnPropertyChanged(nameof(Dot1xMediaProtectionMessage));
        OnPropertyChanged(nameof(HasDot1xMediaProtectionIssue));
        OnPropertyChanged(nameof(WifiMediaProtectionMessage));
        OnPropertyChanged(nameof(HasWifiMediaProtectionIssue));

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
            NetworkConfigurationValidationCode.WiredCertificateMissing or
            NetworkConfigurationValidationCode.WiredMediaProtectionRequired;
    }

    /// <summary>Formats a result for one page: wired codes show only on the Ethernet 802.1X page, the others only on the Wi-Fi page.</summary>
    private string FormatValidationMessage(NetworkConfigurationValidationResult result, bool dot1xOnly)
    {
        if (result.IsValid || dot1xOnly != IsDot1xValidationCode(result.Code))
        {
            return string.Empty;
        }

        return NetworkConfigurationValidationTextFormatter.Format(localizationService, result);
    }
}

/// <summary>One trusted root CA certificate listed on a network page; the content hash of <see cref="Reference"/> is its identity.</summary>
/// <param name="Reference">The certificate held in the authoring file library.</param>
public sealed record TrustedRootCertificateEntry(AuthoringFileReference Reference)
{
    /// <summary>Gets the file name shown in the list.</summary>
    public string FileName => Reference.FileName;

    /// <summary>Returns the file name, which list item automation peers read when no other name is set.</summary>
    public override string ToString() => FileName;
}
