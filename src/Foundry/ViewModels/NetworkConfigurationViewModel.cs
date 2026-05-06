using System.Collections.ObjectModel;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Application;
using Foundry.Core.Services.Configuration;
using Foundry.Services.Configuration;
using Foundry.Services.Localization;
using Microsoft.UI.Xaml;

namespace Foundry.ViewModels;

public sealed partial class NetworkConfigurationViewModel : ObservableObject, IDisposable
{
    private readonly IExpertDeployConfigurationStateService configurationStateService;
    private readonly IFilePickerService filePickerService;
    private readonly IApplicationLocalizationService localizationService;
    private bool isApplyingState = true;
    private bool isSavingState;

    public NetworkConfigurationViewModel(
        IExpertDeployConfigurationStateService configurationStateService,
        IFilePickerService filePickerService,
        IApplicationLocalizationService localizationService)
    {
        this.configurationStateService = configurationStateService;
        this.filePickerService = filePickerService;
        this.localizationService = localizationService;

        RefreshLocalizedText();
        RefreshWifiSecurityTypes();
        ApplyState(configurationStateService.Current.Network);

        localizationService.LanguageChanged += OnLanguageChanged;
        configurationStateService.StateChanged += OnConfigurationStateChanged;
        isApplyingState = false;
    }

    public ObservableCollection<SelectionOption<string>> WifiSecurityTypes { get; } = [];

    [ObservableProperty]
    public partial string PageTitle { get; set; }

    [ObservableProperty]
    public partial string EthernetHeader { get; set; }

    [ObservableProperty]
    public partial string EthernetDescription { get; set; }

    [ObservableProperty]
    public partial string Dot1xEnableText { get; set; }

    [ObservableProperty]
    public partial string ProfileTemplateLabel { get; set; }

    [ObservableProperty]
    public partial string Dot1xCertificateLabel { get; set; }

    [ObservableProperty]
    public partial string RequiresCertificateText { get; set; }

    [ObservableProperty]
    public partial string WifiHeader { get; set; }

    [ObservableProperty]
    public partial string WifiDescription { get; set; }

    [ObservableProperty]
    public partial string WifiProvisionedText { get; set; }

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
    public partial string BrowseButtonText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDot1xSectionEnabled))]
    [NotifyPropertyChangedFor(nameof(IsDot1xCertificatePathEnabled))]
    [NotifyPropertyChangedFor(nameof(Dot1xValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasDot1xValidationError))]
    [NotifyPropertyChangedFor(nameof(Dot1xValidationVisibility))]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasValidationError))]
    public partial bool IsDot1xEnabled { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Dot1xValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasDot1xValidationError))]
    [NotifyPropertyChangedFor(nameof(Dot1xValidationVisibility))]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasValidationError))]
    public partial string Dot1xProfileTemplatePath { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDot1xCertificatePathEnabled))]
    [NotifyPropertyChangedFor(nameof(Dot1xValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasDot1xValidationError))]
    [NotifyPropertyChangedFor(nameof(Dot1xValidationVisibility))]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasValidationError))]
    public partial bool IsDot1xCertificateRequired { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Dot1xValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasDot1xValidationError))]
    [NotifyPropertyChangedFor(nameof(Dot1xValidationVisibility))]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasValidationError))]
    public partial string Dot1xCertificatePath { get; set; } = string.Empty;

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
    [NotifyPropertyChangedFor(nameof(WifiValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasWifiValidationError))]
    [NotifyPropertyChangedFor(nameof(WifiValidationVisibility))]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasValidationError))]
    public partial string WifiEnterpriseProfileTemplatePath { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWifiCertificatePathEnabled))]
    [NotifyPropertyChangedFor(nameof(WifiValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasWifiValidationError))]
    [NotifyPropertyChangedFor(nameof(WifiValidationVisibility))]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasValidationError))]
    public partial bool IsWifiCertificateRequired { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WifiValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasWifiValidationError))]
    [NotifyPropertyChangedFor(nameof(WifiValidationVisibility))]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasValidationError))]
    public partial string WifiCertificatePath { get; set; } = string.Empty;

    public bool IsDot1xSectionEnabled => IsDot1xEnabled;
    public bool IsDot1xCertificatePathEnabled => IsDot1xEnabled && IsDot1xCertificateRequired;
    public bool IsWifiConfigurationSectionEnabled => IsWifiProvisioned && IsWifiConfigured;
    public bool IsWifiSecuritySelectionEnabled => IsWifiConfigurationSectionEnabled;
    public bool IsWifiPersonalSectionEnabled => IsWifiConfigurationSectionEnabled && IsWifiPersonalSelected;
    public bool IsWifiEnterpriseSectionEnabled => IsWifiConfigurationSectionEnabled && IsWifiEnterpriseSelected;
    public bool IsWifiCertificatePathEnabled => IsWifiEnterpriseSectionEnabled && IsWifiCertificateRequired;
    public bool IsWifiPersonalSelected => string.Equals(SelectedWifiSecurityType?.Value, NetworkConfigurationValidator.WifiSecurityPersonal, StringComparison.OrdinalIgnoreCase);
    public bool IsWifiEnterpriseSelected => NetworkConfigurationValidator.IsEnterpriseSecurityType(SelectedWifiSecurityType?.Value);
    public bool HasDot1xValidationError => !string.IsNullOrWhiteSpace(Dot1xValidationMessage);
    public string Dot1xValidationMessage => FormatValidationMessage(NetworkConfigurationValidator.Validate(BuildDot1xOnlySettings()), dot1xOnly: true);
    public Visibility Dot1xValidationVisibility => HasDot1xValidationError ? Visibility.Visible : Visibility.Collapsed;
    public bool HasWifiValidationError => !string.IsNullOrWhiteSpace(WifiValidationMessage);
    public string WifiValidationMessage => FormatValidationMessage(NetworkConfigurationValidator.Validate(BuildWifiOnlySettings()), dot1xOnly: false);
    public Visibility WifiValidationVisibility => HasWifiValidationError ? Visibility.Visible : Visibility.Collapsed;
    public bool HasValidationError => HasDot1xValidationError || HasWifiValidationError;
    public string ValidationMessage => HasDot1xValidationError ? Dot1xValidationMessage : WifiValidationMessage;

    public void Dispose()
    {
        localizationService.LanguageChanged -= OnLanguageChanged;
        configurationStateService.StateChanged -= OnConfigurationStateChanged;
    }

    [RelayCommand]
    private async Task BrowseDot1xProfileTemplateAsync()
    {
        string? path = await PickOpenFileAsync("Network.ProfileTemplatePickerTitle", [".xml", "*"]);
        if (!string.IsNullOrWhiteSpace(path))
        {
            Dot1xProfileTemplatePath = path;
        }
    }

    [RelayCommand]
    private async Task BrowseDot1xCertificateAsync()
    {
        string? path = await PickOpenFileAsync("Dot1x.CertificatePickerTitle", [".cer", ".crt", ".pfx", "*"]);
        if (!string.IsNullOrWhiteSpace(path))
        {
            Dot1xCertificatePath = path;
        }
    }

    [RelayCommand]
    private async Task BrowseWifiEnterpriseProfileTemplateAsync()
    {
        string? path = await PickOpenFileAsync("Network.ProfileTemplatePickerTitle", [".xml", "*"]);
        if (!string.IsNullOrWhiteSpace(path))
        {
            WifiEnterpriseProfileTemplatePath = path;
        }
    }

    [RelayCommand]
    private async Task BrowseWifiCertificateAsync()
    {
        string? path = await PickOpenFileAsync("Wifi.CertificatePickerTitle", [".cer", ".crt", ".pfx", "*"]);
        if (!string.IsNullOrWhiteSpace(path))
        {
            WifiCertificatePath = path;
        }
    }

    partial void OnIsDot1xEnabledChanged(bool value)
    {
        if (!value)
        {
            Dot1xProfileTemplatePath = string.Empty;
            IsDot1xCertificateRequired = false;
            Dot1xCertificatePath = string.Empty;
        }

        SaveState();
    }

    partial void OnDot1xProfileTemplatePathChanged(string value)
    {
        SaveState();
    }

    partial void OnIsDot1xCertificateRequiredChanged(bool value)
    {
        if (!value)
        {
            Dot1xCertificatePath = string.Empty;
        }

        SaveState();
    }

    partial void OnDot1xCertificatePathChanged(string value)
    {
        SaveState();
    }

    partial void OnIsWifiProvisionedChanged(bool value)
    {
        if (!value)
        {
            IsWifiConfigured = false;
        }

        SaveState();
    }

    partial void OnIsWifiConfiguredChanged(bool value)
    {
        if (!value)
        {
            WifiSsid = string.Empty;
            SelectedWifiSecurityType = SelectWifiSecurityOption(NetworkConfigurationValidator.WifiSecurityPersonal);
            WifiPassphrase = string.Empty;
            WifiEnterpriseProfileTemplatePath = string.Empty;
            IsWifiCertificateRequired = false;
            WifiCertificatePath = string.Empty;
        }

        SaveState();
    }

    partial void OnWifiSsidChanged(string value)
    {
        SaveState();
    }

    partial void OnSelectedWifiSecurityTypeChanged(SelectionOption<string>? value)
    {
        if (!IsWifiPersonalSelected)
        {
            WifiPassphrase = string.Empty;
        }

        if (!IsWifiEnterpriseSelected)
        {
            WifiEnterpriseProfileTemplatePath = string.Empty;
            IsWifiCertificateRequired = false;
            WifiCertificatePath = string.Empty;
        }

        OnPropertyChanged(nameof(IsWifiPersonalSelected));
        OnPropertyChanged(nameof(IsWifiEnterpriseSelected));
        SaveState();
    }

    partial void OnWifiPassphraseChanged(string value)
    {
        SaveState();
    }

    partial void OnWifiEnterpriseProfileTemplatePathChanged(string value)
    {
        SaveState();
    }

    partial void OnIsWifiCertificateRequiredChanged(bool value)
    {
        if (!value)
        {
            WifiCertificatePath = string.Empty;
        }

        SaveState();
    }

    partial void OnWifiCertificatePathChanged(string value)
    {
        SaveState();
    }

    private async Task<string?> PickOpenFileAsync(string titleKey, IReadOnlyList<string> filters)
    {
        return await filePickerService.PickOpenFileAsync(
            new FileOpenPickerRequest(localizationService.GetString(titleKey), filters));
    }

    private void ApplyState(NetworkSettings settings)
    {
        isApplyingState = true;
        IsDot1xEnabled = settings.Dot1x.IsEnabled;
        Dot1xProfileTemplatePath = settings.Dot1x.ProfileTemplatePath ?? string.Empty;
        IsDot1xCertificateRequired = settings.Dot1x.RequiresCertificate;
        Dot1xCertificatePath = settings.Dot1x.CertificatePath ?? string.Empty;

        IsWifiProvisioned = settings.WifiProvisioned || settings.Wifi.IsEnabled;
        IsWifiConfigured = settings.Wifi.IsEnabled;
        WifiSsid = settings.Wifi.Ssid ?? string.Empty;
        SelectedWifiSecurityType = SelectWifiSecurityOption(NetworkConfigurationValidator.NormalizeWifiSecurityType(settings.Wifi));
        WifiPassphrase = settings.Wifi.Passphrase ?? string.Empty;
        WifiEnterpriseProfileTemplatePath = settings.Wifi.EnterpriseProfileTemplatePath ?? string.Empty;
        IsWifiCertificateRequired = settings.Wifi.RequiresCertificate;
        WifiCertificatePath = settings.Wifi.CertificatePath ?? string.Empty;
        isApplyingState = false;
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
            configurationStateService.UpdateNetwork(BuildSettingsForValidation());
        }
        finally
        {
            isSavingState = false;
        }
    }

    private NetworkSettings BuildSettingsForValidation()
    {
        return new NetworkSettings
        {
            Dot1x = new Dot1xSettings
            {
                IsEnabled = IsDot1xEnabled,
                ProfileTemplatePath = IsDot1xEnabled && !string.IsNullOrWhiteSpace(Dot1xProfileTemplatePath)
                    ? Dot1xProfileTemplatePath.Trim()
                    : null,
                AuthenticationMode = NetworkAuthenticationMode.MachineOnly,
                AllowRuntimeCredentials = false,
                RequiresCertificate = IsDot1xEnabled && IsDot1xCertificateRequired,
                CertificatePath = IsDot1xCertificatePathEnabled && !string.IsNullOrWhiteSpace(Dot1xCertificatePath)
                    ? Dot1xCertificatePath.Trim()
                    : null
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
                Passphrase = IsWifiPersonalSectionEnabled && !string.IsNullOrWhiteSpace(WifiPassphrase)
                    ? WifiPassphrase.Trim()
                    : null,
                HasEnterpriseProfile = IsWifiEnterpriseSectionEnabled,
                EnterpriseProfileTemplatePath = IsWifiEnterpriseSectionEnabled && !string.IsNullOrWhiteSpace(WifiEnterpriseProfileTemplatePath)
                    ? WifiEnterpriseProfileTemplatePath.Trim()
                    : null,
                EnterpriseAuthenticationMode = NetworkAuthenticationMode.UserOnly,
                AllowRuntimeCredentials = false,
                RequiresCertificate = IsWifiEnterpriseSectionEnabled && IsWifiCertificateRequired,
                CertificatePath = IsWifiCertificatePathEnabled && !string.IsNullOrWhiteSpace(WifiCertificatePath)
                    ? WifiCertificatePath.Trim()
                    : null
            }
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
        PageTitle = localizationService.GetString("NetworkPage_Title.Text");
        EthernetHeader = localizationService.GetString("Network.EthernetSectionTitle");
        EthernetDescription = localizationService.GetString("Network.EthernetHelperText");
        Dot1xEnableText = localizationService.GetString("Dot1x.EnableLabel");
        ProfileTemplateLabel = localizationService.GetString("Network.ProfileTemplateLabel");
        Dot1xCertificateLabel = localizationService.GetString("Dot1x.CertificateLabel");
        RequiresCertificateText = localizationService.GetString("Network.RequiresCertificateLabel");
        WifiHeader = localizationService.GetString("Network.WifiSectionTitle");
        WifiDescription = localizationService.GetString("Network.WifiHelperText");
        WifiProvisionedText = localizationService.GetString("Wifi.EnableLabel");
        WifiConfiguredText = localizationService.GetString("Wifi.ConfigureLabel");
        WifiSsidLabel = localizationService.GetString("Wifi.SsidLabel");
        WifiSecurityTypeLabel = localizationService.GetString("Wifi.SecurityTypeLabel");
        WifiPassphraseLabel = localizationService.GetString("Wifi.PassphraseLabel");
        WifiPersonalDescription = localizationService.GetString("Network.WifiPersonalHelperText");
        WifiEnterpriseHeader = localizationService.GetString("Network.WifiEnterpriseAdvancedHeader");
        WifiEnterpriseDescription = localizationService.GetString("Network.EnterpriseTemplateDrivenHelperText");
        WifiEnterpriseTemplateText = localizationService.GetString("Network.WifiEnterpriseTemplateLabel");
        WifiCertificateLabel = localizationService.GetString("Wifi.CertificateLabel");
        BrowseButtonText = localizationService.GetString("Common.Browse");
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

    private string FormatValidationMessage(NetworkConfigurationValidationResult result, bool dot1xOnly)
    {
        if (result.IsValid)
        {
            return string.Empty;
        }

        if (dot1xOnly && result.Code is not (NetworkConfigurationValidationCode.WiredProfileTemplateRequired or NetworkConfigurationValidationCode.WiredCertificateRequired))
        {
            return string.Empty;
        }

        if (!dot1xOnly && result.Code is NetworkConfigurationValidationCode.WiredProfileTemplateRequired or NetworkConfigurationValidationCode.WiredCertificateRequired)
        {
            return string.Empty;
        }

        string key = result.Code switch
        {
            NetworkConfigurationValidationCode.WifiProvisioningRequired => "Network.ErrorWifiProvisioningRequired",
            NetworkConfigurationValidationCode.WiredProfileTemplateRequired => "Network.ErrorWiredProfileTemplateRequired",
            NetworkConfigurationValidationCode.WiredCertificateRequired => "Network.ErrorWiredCertificateRequired",
            NetworkConfigurationValidationCode.WifiSsidRequired => "Network.ErrorWifiSsidRequired",
            NetworkConfigurationValidationCode.UnsupportedWifiSecurityType => "Network.ErrorUnsupportedWifiSecurityTypeFormat",
            NetworkConfigurationValidationCode.WifiPersonalPassphraseInvalid => "Network.ErrorWifiPersonalPassphraseInvalid",
            NetworkConfigurationValidationCode.WifiEnterpriseProfileTemplateRequired => "Network.ErrorWifiEnterpriseProfileTemplateRequired",
            NetworkConfigurationValidationCode.WifiEnterpriseProfileTemplateMissing => "Network.ErrorWifiEnterpriseProfileTemplateMissing",
            NetworkConfigurationValidationCode.WifiEnterpriseAuthenticationUnsupported => "Network.ErrorWifiEnterpriseAuthenticationUnsupported",
            NetworkConfigurationValidationCode.WifiEnterpriseAuthenticationMismatch => "Network.ErrorWifiEnterpriseAuthenticationMismatchFormat",
            NetworkConfigurationValidationCode.WifiEnterpriseCertificateRequired => "Network.ErrorWifiEnterpriseCertificateRequired",
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        object[] arguments = result.Code == NetworkConfigurationValidationCode.WifiEnterpriseAuthenticationMismatch
            ? result.FormatArguments.Select(FormatEnterpriseSecurityTypeLabel).Cast<object>().ToArray()
            : result.FormatArguments.Cast<object>().ToArray();

        return result.FormatArguments.Count == 0
            ? localizationService.GetString(key)
            : localizationService.FormatString(key, arguments);
    }

    private string FormatEnterpriseSecurityTypeLabel(string securityType)
    {
        return securityType switch
        {
            NetworkConfigurationValidator.WifiSecurityEnterpriseWpa3 => localizationService.GetString("Wifi.SecurityTypeEnterpriseWpa3"),
            NetworkConfigurationValidator.WifiSecurityEnterpriseWpa3192 => localizationService.GetString("Wifi.SecurityTypeEnterpriseWpa3192"),
            _ => localizationService.GetString("Wifi.SecurityTypeEnterprise")
        };
    }
}
