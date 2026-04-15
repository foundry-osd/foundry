using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Foundry.Models.Configuration;
using Foundry.Services.ApplicationShell;
using Foundry.Services.Localization;
using Foundry.Services.Operations;
using System.Collections.ObjectModel;
using System.Xml.Linq;

namespace Foundry.ViewModels;

public partial class NetworkSettingsViewModel : LocalizedViewModelBase
{
    private const string WifiSecurityOpen = "Open";
    private const string WifiSecurityOwe = "OWE";
    private const string WifiSecurityPersonal = "WPA2/WPA3-Personal";
    private const string WifiSecurityEnterprise = "WPA2/WPA3-Enterprise";
    private const string WifiSecurityEnterpriseWpa3 = "WPA3ENT";
    private const string WifiSecurityEnterpriseWpa3192 = "WPA3ENT192";
    private static readonly string[] LegacyWifiSecurityPersonalValues = ["WPA2-Personal", "WPA3-Personal", "Personal"];
    private static readonly string[] LegacyWifiSecurityEnterpriseValues = ["WPA2-Enterprise", "WPA3-Enterprise", "WPA3", "Enterprise"];

    private readonly IApplicationShellService _applicationShellService;
    private readonly IOperationProgressService _operationProgressService;

    public NetworkSettingsViewModel(
        ILocalizationService localizationService,
        IApplicationShellService applicationShellService,
        IOperationProgressService operationProgressService)
        : base(localizationService)
    {
        _applicationShellService = applicationShellService ?? throw new ArgumentNullException(nameof(applicationShellService));
        _operationProgressService = operationProgressService ?? throw new ArgumentNullException(nameof(operationProgressService));
        _operationProgressService.ProgressChanged += OnOperationProgressChanged;
        LocalizationService.LanguageChanged += OnLocalizationLanguageChanged;
        RefreshOptionLists();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDot1xSectionEnabled))]
    [NotifyPropertyChangedFor(nameof(IsDot1xCertificatePathEnabled))]
    [NotifyPropertyChangedFor(nameof(Dot1xValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasDot1xValidationError))]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasValidationError))]
    private bool isDot1xEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Dot1xValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasDot1xValidationError))]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasValidationError))]
    private string dot1xProfileTemplatePath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDot1xCertificatePathEnabled))]
    [NotifyPropertyChangedFor(nameof(Dot1xValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasDot1xValidationError))]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasValidationError))]
    private bool isDot1xCertificateRequired;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Dot1xValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasDot1xValidationError))]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasValidationError))]
    private string dot1xCertificatePath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWifiConfigurationSectionEnabled))]
    [NotifyPropertyChangedFor(nameof(IsWifiSecuritySelectionEnabled))]
    [NotifyPropertyChangedFor(nameof(IsWifiPersonalSectionEnabled))]
    [NotifyPropertyChangedFor(nameof(IsWifiEnterpriseSectionEnabled))]
    [NotifyPropertyChangedFor(nameof(IsWifiCertificatePathEnabled))]
    [NotifyPropertyChangedFor(nameof(WifiValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasWifiValidationError))]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasValidationError))]
    private bool isWifiProvisioned;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWifiConfigurationSectionEnabled))]
    [NotifyPropertyChangedFor(nameof(IsWifiSecuritySelectionEnabled))]
    [NotifyPropertyChangedFor(nameof(IsWifiPersonalSectionEnabled))]
    [NotifyPropertyChangedFor(nameof(IsWifiEnterpriseSectionEnabled))]
    [NotifyPropertyChangedFor(nameof(IsWifiCertificatePathEnabled))]
    [NotifyPropertyChangedFor(nameof(WifiValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasWifiValidationError))]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasValidationError))]
    private bool isWifiConfigured;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WifiValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasWifiValidationError))]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasValidationError))]
    private string wifiSsid = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWifiPersonalSectionEnabled))]
    [NotifyPropertyChangedFor(nameof(IsWifiEnterpriseSectionEnabled))]
    [NotifyPropertyChangedFor(nameof(IsWifiCertificatePathEnabled))]
    [NotifyPropertyChangedFor(nameof(IsWifiOpenSelected))]
    [NotifyPropertyChangedFor(nameof(IsWifiPersonalSelected))]
    [NotifyPropertyChangedFor(nameof(IsWifiEnterpriseSelected))]
    [NotifyPropertyChangedFor(nameof(WifiValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasWifiValidationError))]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasValidationError))]
    private string wifiSecurityType = WifiSecurityPersonal;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WifiValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasWifiValidationError))]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasValidationError))]
    private string wifiPassphrase = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WifiValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasWifiValidationError))]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasValidationError))]
    private string wifiEnterpriseProfileTemplatePath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWifiCertificatePathEnabled))]
    [NotifyPropertyChangedFor(nameof(WifiValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasWifiValidationError))]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasValidationError))]
    private bool isWifiCertificateRequired;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WifiValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasWifiValidationError))]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasValidationError))]
    private string wifiCertificatePath = string.Empty;

    public ObservableCollection<SecurityTypeOption> WifiSecurityTypes { get; } = [];

    public bool IsDot1xSectionEnabled => IsDot1xEnabled;
    public bool IsDot1xCertificatePathEnabled => IsDot1xEnabled && IsDot1xCertificateRequired;
    public bool IsWifiConfigurationSectionEnabled => IsWifiProvisioned && IsWifiConfigured;
    public bool IsWifiSecuritySelectionEnabled => IsWifiConfigurationSectionEnabled;
    public bool IsWifiPersonalSectionEnabled => IsWifiConfigurationSectionEnabled && IsWifiPersonalSelected;
    public bool IsWifiEnterpriseSectionEnabled => IsWifiConfigurationSectionEnabled && IsWifiEnterpriseSelected;
    public bool IsWifiCertificatePathEnabled => IsWifiEnterpriseSectionEnabled && IsWifiCertificateRequired;
    public bool IsWifiOpenSelected => string.Equals(WifiSecurityType, WifiSecurityOpen, StringComparison.OrdinalIgnoreCase);
    public bool IsWifiPersonalSelected => string.Equals(WifiSecurityType, WifiSecurityPersonal, StringComparison.OrdinalIgnoreCase);
    public bool IsWifiEnterpriseSelected => IsEnterpriseSecurityType(WifiSecurityType);
    public bool HasDot1xValidationError => !string.IsNullOrWhiteSpace(Dot1xValidationMessage);
    public string Dot1xValidationMessage => BuildDot1xValidationMessage();
    public bool HasWifiValidationError => !string.IsNullOrWhiteSpace(WifiValidationMessage);
    public string WifiValidationMessage => BuildWifiValidationMessage();
    public bool HasValidationError => HasDot1xValidationError || HasWifiValidationError;
    public string ValidationMessage => HasDot1xValidationError ? Dot1xValidationMessage : WifiValidationMessage;

    partial void OnIsDot1xEnabledChanged(bool value)
    {
        if (!value)
        {
            Dot1xProfileTemplatePath = string.Empty;
            IsDot1xCertificateRequired = false;
            Dot1xCertificatePath = string.Empty;
        }
    }

    partial void OnIsDot1xCertificateRequiredChanged(bool value)
    {
        if (!value)
        {
            Dot1xCertificatePath = string.Empty;
        }
    }

    partial void OnIsWifiProvisionedChanged(bool value)
    {
        if (!value)
        {
            IsWifiConfigured = false;
        }
    }

    partial void OnIsWifiConfiguredChanged(bool value)
    {
        if (!value)
        {
            WifiSsid = string.Empty;
            WifiSecurityType = WifiSecurityPersonal;
            WifiPassphrase = string.Empty;
            WifiEnterpriseProfileTemplatePath = string.Empty;
            IsWifiCertificateRequired = false;
            WifiCertificatePath = string.Empty;
        }
    }

    partial void OnWifiSecurityTypeChanged(string value)
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
    }

    partial void OnIsWifiCertificateRequiredChanged(bool value)
    {
        if (!value)
        {
            WifiCertificatePath = string.Empty;
        }
    }

    [RelayCommand(CanExecute = nameof(CanBrowseFiles))]
    private void BrowseDot1xProfileTemplate()
    {
        string? selectedPath = _applicationShellService.PickOpenFilePath(
            Strings["Network.ProfileTemplatePickerTitle"],
            Strings["Network.ProfileTemplatePickerFilter"]);
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            Dot1xProfileTemplatePath = selectedPath;
        }
    }

    [RelayCommand(CanExecute = nameof(CanBrowseFiles))]
    private void BrowseDot1xCertificate()
    {
        string? selectedPath = _applicationShellService.PickOpenFilePath(
            Strings["Dot1x.CertificatePickerTitle"],
            Strings["Common.CertificatePickerFilter"]);
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            Dot1xCertificatePath = selectedPath;
        }
    }

    [RelayCommand(CanExecute = nameof(CanBrowseFiles))]
    private void BrowseWifiEnterpriseProfileTemplate()
    {
        string? selectedPath = _applicationShellService.PickOpenFilePath(
            Strings["Network.ProfileTemplatePickerTitle"],
            Strings["Network.ProfileTemplatePickerFilter"]);
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            WifiEnterpriseProfileTemplatePath = selectedPath;
        }
    }

    [RelayCommand(CanExecute = nameof(CanBrowseFiles))]
    private void BrowseWifiCertificate()
    {
        string? selectedPath = _applicationShellService.PickOpenFilePath(
            Strings["Wifi.CertificatePickerTitle"],
            Strings["Common.CertificatePickerFilter"]);
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            WifiCertificatePath = selectedPath;
        }
    }

    public NetworkSettings BuildSettings()
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
                CertificatePath = IsDot1xEnabled && IsDot1xCertificateRequired && !string.IsNullOrWhiteSpace(Dot1xCertificatePath)
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
                SecurityType = IsWifiConfigured && !string.IsNullOrWhiteSpace(WifiSecurityType)
                    ? WifiSecurityType.Trim()
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
                CertificatePath = IsWifiEnterpriseSectionEnabled && IsWifiCertificateRequired && !string.IsNullOrWhiteSpace(WifiCertificatePath)
                    ? WifiCertificatePath.Trim()
                    : null
            }
        };
    }

    public void ApplySettings(NetworkSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        IsDot1xEnabled = settings.Dot1x.IsEnabled;
        Dot1xProfileTemplatePath = settings.Dot1x.ProfileTemplatePath ?? string.Empty;
        IsDot1xCertificateRequired = settings.Dot1x.RequiresCertificate;
        Dot1xCertificatePath = settings.Dot1x.CertificatePath ?? string.Empty;

        IsWifiProvisioned = settings.WifiProvisioned || settings.Wifi.IsEnabled;
        IsWifiConfigured = settings.Wifi.IsEnabled;
        WifiSsid = settings.Wifi.Ssid ?? string.Empty;
        WifiSecurityType = NormalizeWifiSecurityType(settings.Wifi);
        WifiPassphrase = settings.Wifi.Passphrase ?? string.Empty;
        WifiEnterpriseProfileTemplatePath = settings.Wifi.EnterpriseProfileTemplatePath ?? string.Empty;
        IsWifiCertificateRequired = settings.Wifi.RequiresCertificate;
        WifiCertificatePath = settings.Wifi.CertificatePath ?? string.Empty;

    }

    public override void Dispose()
    {
        _operationProgressService.ProgressChanged -= OnOperationProgressChanged;
        LocalizationService.LanguageChanged -= OnLocalizationLanguageChanged;
        base.Dispose();
    }

    private bool CanBrowseFiles()
    {
        return !_operationProgressService.IsOperationInProgress;
    }

    private void OnOperationProgressChanged(object? sender, EventArgs e)
    {
        RunOnUiThread(() =>
        {
            BrowseDot1xProfileTemplateCommand.NotifyCanExecuteChanged();
            BrowseDot1xCertificateCommand.NotifyCanExecuteChanged();
            BrowseWifiEnterpriseProfileTemplateCommand.NotifyCanExecuteChanged();
            BrowseWifiCertificateCommand.NotifyCanExecuteChanged();
        });
    }

    private void OnLocalizationLanguageChanged(object? sender, EventArgs e)
    {
        RunOnUiThread(RefreshOptionLists);
    }

    private void RefreshOptionLists()
    {
        ReplaceCollection(WifiSecurityTypes,
        [
            new SecurityTypeOption(WifiSecurityOpen, Strings["Wifi.SecurityTypeOpen"]),
            new SecurityTypeOption(WifiSecurityOwe, Strings["Wifi.SecurityTypeOwe"]),
            new SecurityTypeOption(WifiSecurityPersonal, Strings["Wifi.SecurityTypePersonal"]),
            new SecurityTypeOption(WifiSecurityEnterprise, Strings["Wifi.SecurityTypeEnterprise"]),
            new SecurityTypeOption(WifiSecurityEnterpriseWpa3, Strings["Wifi.SecurityTypeEnterpriseWpa3"]),
            new SecurityTypeOption(WifiSecurityEnterpriseWpa3192, Strings["Wifi.SecurityTypeEnterpriseWpa3192"])
        ]);

        OnPropertyChanged(nameof(WifiSecurityTypes));
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> collection, IReadOnlyList<T> items)
    {
        collection.Clear();
        foreach (T item in items)
        {
            collection.Add(item);
        }
    }

    private static string NormalizeWifiSecurityType(WifiSettings settings)
    {
        if (string.Equals(settings.SecurityType, WifiSecurityOpen, StringComparison.OrdinalIgnoreCase))
        {
            return WifiSecurityOpen;
        }

        if (string.Equals(settings.SecurityType, WifiSecurityOwe, StringComparison.OrdinalIgnoreCase))
        {
            return WifiSecurityOwe;
        }

        if (string.Equals(settings.SecurityType, WifiSecurityPersonal, StringComparison.OrdinalIgnoreCase) ||
            LegacyWifiSecurityPersonalValues.Any(value => string.Equals(settings.SecurityType, value, StringComparison.OrdinalIgnoreCase)))
        {
            return WifiSecurityPersonal;
        }

        string? enterpriseSecurityType = NormalizeEnterpriseSecurityType(settings.SecurityType);
        if (enterpriseSecurityType is not null || settings.HasEnterpriseProfile)
        {
            return enterpriseSecurityType ?? WifiSecurityEnterprise;
        }

        if (!string.IsNullOrWhiteSpace(settings.Passphrase))
        {
            return WifiSecurityPersonal;
        }

        return WifiSecurityOpen;
    }

    private static bool IsEnterpriseSecurityType(string? securityType)
    {
        return NormalizeEnterpriseSecurityType(securityType) is not null;
    }

    private static string? NormalizeEnterpriseSecurityType(string? securityType)
    {
        if (string.IsNullOrWhiteSpace(securityType))
        {
            return null;
        }

        if (string.Equals(securityType, WifiSecurityEnterprise, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(securityType, "WPA2-Enterprise", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(securityType, "Enterprise", StringComparison.OrdinalIgnoreCase))
        {
            return WifiSecurityEnterprise;
        }

        if (string.Equals(securityType, WifiSecurityEnterpriseWpa3, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(securityType, "WPA3-Enterprise", StringComparison.OrdinalIgnoreCase))
        {
            return WifiSecurityEnterpriseWpa3;
        }

        if (string.Equals(securityType, WifiSecurityEnterpriseWpa3192, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(securityType, "WPA3", StringComparison.OrdinalIgnoreCase))
        {
            return WifiSecurityEnterpriseWpa3192;
        }

        return LegacyWifiSecurityEnterpriseValues.Any(value => string.Equals(securityType, value, StringComparison.OrdinalIgnoreCase))
            ? WifiSecurityEnterprise
            : null;
    }

    private string BuildDot1xValidationMessage()
    {
        if (IsDot1xEnabled)
        {
            if (string.IsNullOrWhiteSpace(Dot1xProfileTemplatePath))
            {
                return "Wired 802.1X requires a wired profile template.";
            }

            if (IsDot1xCertificateRequired && string.IsNullOrWhiteSpace(Dot1xCertificatePath))
            {
                return "Wired 802.1X requires a trusted root CA certificate file when certificate trust is enabled.";
            }
        }

        return string.Empty;
    }

    private string BuildWifiValidationMessage()
    {
        if (!IsWifiProvisioned || !IsWifiConfigured)
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(WifiSsid))
        {
            return "Wi-Fi configuration requires an SSID.";
        }

        if (IsWifiPersonalSelected)
        {
            string passphrase = WifiPassphrase.Trim();
            if (passphrase.Length < 8 || passphrase.Length > 63)
            {
                return "Personal Wi-Fi requires an 8 to 63 character passphrase.";
            }
        }

        if (IsWifiEnterpriseSelected)
        {
            if (string.IsNullOrWhiteSpace(WifiEnterpriseProfileTemplatePath))
            {
                return "Enterprise Wi-Fi requires a profile template.";
            }

            string trimmedTemplatePath = WifiEnterpriseProfileTemplatePath.Trim();
            if (!File.Exists(Path.GetFullPath(trimmedTemplatePath)))
            {
                return "Enterprise Wi-Fi profile template file was not found.";
            }

            if (RequiresExplicitEnterpriseTemplateAuthentication(WifiSecurityType))
            {
                string? templateSecurityType = TryReadEnterpriseTemplateSecurityType(trimmedTemplatePath);
                if (templateSecurityType is null)
                {
                    return "Enterprise Wi-Fi profile template must contain a supported enterprise authentication value.";
                }

                if (!string.Equals(templateSecurityType, WifiSecurityType, StringComparison.OrdinalIgnoreCase))
                {
                    return $"Selected enterprise Wi-Fi security type does not match the profile template authentication ({FormatEnterpriseSecurityTypeLabel(templateSecurityType)}).";
                }
            }

            if (IsWifiCertificateRequired && string.IsNullOrWhiteSpace(WifiCertificatePath))
            {
                return "Enterprise Wi-Fi requires a trusted root CA certificate file when certificate trust is enabled.";
            }
        }

        return string.Empty;
    }

    private static bool RequiresExplicitEnterpriseTemplateAuthentication(string? securityType)
    {
        return string.Equals(securityType, WifiSecurityEnterpriseWpa3, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(securityType, WifiSecurityEnterpriseWpa3192, StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryReadEnterpriseTemplateSecurityType(string profileTemplatePath)
    {
        try
        {
            XDocument document = XDocument.Load(Path.GetFullPath(profileTemplatePath));
            XNamespace wlanProfile = "http://www.microsoft.com/networking/WLAN/profile/v1";
            string? authentication = document
                .Descendants(wlanProfile + "authentication")
                .Select(static element => element.Value?.Trim())
                .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));

            return NormalizeEnterpriseSecurityType(authentication);
        }
        catch
        {
            return null;
        }
    }

    private static string FormatEnterpriseSecurityTypeLabel(string securityType)
    {
        return securityType switch
        {
            WifiSecurityEnterpriseWpa3 => "WPA3 Enterprise",
            WifiSecurityEnterpriseWpa3192 => "WPA3 Enterprise 192-bit",
            _ => "WPA2/WPA3 Enterprise"
        };
    }
    public sealed record SecurityTypeOption(string Value, string DisplayName);
}
