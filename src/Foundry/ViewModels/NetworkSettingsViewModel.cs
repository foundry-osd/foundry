using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Foundry.Models.Configuration;
using Foundry.Services.ApplicationShell;
using Foundry.Services.Localization;
using Foundry.Services.Operations;
using System.Collections.ObjectModel;

namespace Foundry.ViewModels;

public partial class NetworkSettingsViewModel : LocalizedViewModelBase
{
    private const string WifiSecurityOpen = "Open";
    private const string WifiSecurityPersonal = "WPA2-Personal";
    private const string WifiSecurityEnterprise = "WPA2/WPA3-Enterprise";
    private static readonly string[] LegacyWifiSecurityPersonalValues = ["WPA2-Personal", "WPA3-Personal", "Personal"];
    private static readonly string[] LegacyWifiSecurityEnterpriseValues = ["WPA2-Enterprise", "WPA3-Enterprise", "Enterprise"];

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
    private bool isDot1xEnabled;

    [ObservableProperty]
    private string dot1xProfileTemplatePath = string.Empty;

    [ObservableProperty]
    private bool isDot1xCertificateRequired;

    [ObservableProperty]
    private string dot1xCertificatePath = string.Empty;

    [ObservableProperty]
    private bool isWifiProvisioned;

    [ObservableProperty]
    private bool isWifiConfigured;

    [ObservableProperty]
    private string wifiSsid = string.Empty;

    [ObservableProperty]
    private string wifiSecurityType = WifiSecurityPersonal;

    [ObservableProperty]
    private string wifiPassphrase = string.Empty;

    [ObservableProperty]
    private string wifiEnterpriseProfileTemplatePath = string.Empty;

    [ObservableProperty]
    private bool isWifiCertificateRequired;

    [ObservableProperty]
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
    public bool IsWifiEnterpriseSelected => string.Equals(WifiSecurityType, WifiSecurityEnterprise, StringComparison.OrdinalIgnoreCase);
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

        RaiseDot1xStateProperties();
    }

    partial void OnIsDot1xCertificateRequiredChanged(bool value)
    {
        if (!value)
        {
            Dot1xCertificatePath = string.Empty;
        }

        OnPropertyChanged(nameof(IsDot1xCertificatePathEnabled));
    }

    partial void OnIsWifiProvisionedChanged(bool value)
    {
        if (!value)
        {
            IsWifiConfigured = false;
        }

        RaiseWifiStateProperties();
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

        RaiseWifiStateProperties();
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

        RaiseWifiStateProperties();
    }

    partial void OnIsWifiCertificateRequiredChanged(bool value)
    {
        if (!value)
        {
            WifiCertificatePath = string.Empty;
        }

        OnPropertyChanged(nameof(IsWifiCertificatePathEnabled));
    }

    [RelayCommand(CanExecute = nameof(CanBrowseFiles))]
    private void BrowseDot1xProfileTemplate()
    {
        string? selectedPath = _applicationShellService.PickOpenFilePath(
            Strings["NetworkProfileTemplatePickerTitle"],
            Strings["NetworkProfileTemplatePickerFilter"]);
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            Dot1xProfileTemplatePath = selectedPath;
        }
    }

    [RelayCommand(CanExecute = nameof(CanBrowseFiles))]
    private void BrowseDot1xCertificate()
    {
        string? selectedPath = _applicationShellService.PickOpenFilePath(
            Strings["Dot1xCertificatePickerTitle"],
            Strings["CertificatePickerFilter"]);
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            Dot1xCertificatePath = selectedPath;
        }
    }

    [RelayCommand(CanExecute = nameof(CanBrowseFiles))]
    private void BrowseWifiEnterpriseProfileTemplate()
    {
        string? selectedPath = _applicationShellService.PickOpenFilePath(
            Strings["NetworkProfileTemplatePickerTitle"],
            Strings["NetworkProfileTemplatePickerFilter"]);
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            WifiEnterpriseProfileTemplatePath = selectedPath;
        }
    }

    [RelayCommand(CanExecute = nameof(CanBrowseFiles))]
    private void BrowseWifiCertificate()
    {
        string? selectedPath = _applicationShellService.PickOpenFilePath(
            Strings["WifiCertificatePickerTitle"],
            Strings["CertificatePickerFilter"]);
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

        RaiseDot1xStateProperties();
        RaiseWifiStateProperties();
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

    private void RaiseDot1xStateProperties()
    {
        OnPropertyChanged(nameof(IsDot1xSectionEnabled));
        OnPropertyChanged(nameof(IsDot1xCertificatePathEnabled));
        OnPropertyChanged(nameof(Dot1xValidationMessage));
        OnPropertyChanged(nameof(HasDot1xValidationError));
        OnPropertyChanged(nameof(ValidationMessage));
        OnPropertyChanged(nameof(HasValidationError));
    }

    private void RaiseWifiStateProperties()
    {
        OnPropertyChanged(nameof(IsWifiConfigurationSectionEnabled));
        OnPropertyChanged(nameof(IsWifiSecuritySelectionEnabled));
        OnPropertyChanged(nameof(IsWifiPersonalSectionEnabled));
        OnPropertyChanged(nameof(IsWifiEnterpriseSectionEnabled));
        OnPropertyChanged(nameof(IsWifiCertificatePathEnabled));
        OnPropertyChanged(nameof(IsWifiOpenSelected));
        OnPropertyChanged(nameof(IsWifiPersonalSelected));
        OnPropertyChanged(nameof(IsWifiEnterpriseSelected));
        OnPropertyChanged(nameof(WifiValidationMessage));
        OnPropertyChanged(nameof(HasWifiValidationError));
        OnPropertyChanged(nameof(ValidationMessage));
        OnPropertyChanged(nameof(HasValidationError));
    }

    private void RefreshOptionLists()
    {
        ReplaceCollection(WifiSecurityTypes,
        [
            new SecurityTypeOption(WifiSecurityOpen, Strings["WifiSecurityTypeOpen"]),
            new SecurityTypeOption(WifiSecurityPersonal, Strings["WifiSecurityTypePersonal"]),
            new SecurityTypeOption(WifiSecurityEnterprise, Strings["WifiSecurityTypeEnterprise"])
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

        if (string.Equals(settings.SecurityType, WifiSecurityPersonal, StringComparison.OrdinalIgnoreCase) ||
            LegacyWifiSecurityPersonalValues.Any(value => string.Equals(settings.SecurityType, value, StringComparison.OrdinalIgnoreCase)))
        {
            return WifiSecurityPersonal;
        }

        if (string.Equals(settings.SecurityType, WifiSecurityEnterprise, StringComparison.OrdinalIgnoreCase) ||
            LegacyWifiSecurityEnterpriseValues.Any(value => string.Equals(settings.SecurityType, value, StringComparison.OrdinalIgnoreCase)) ||
            settings.HasEnterpriseProfile)
        {
            return WifiSecurityEnterprise;
        }

        if (!string.IsNullOrWhiteSpace(settings.Passphrase))
        {
            return WifiSecurityPersonal;
        }

        return WifiSecurityOpen;
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
                return "WPA2 Personal Wi-Fi requires an 8 to 63 character passphrase.";
            }
        }

        if (IsWifiEnterpriseSelected)
        {
            if (string.IsNullOrWhiteSpace(WifiEnterpriseProfileTemplatePath))
            {
                return "Enterprise Wi-Fi requires a profile template.";
            }

            if (IsWifiCertificateRequired && string.IsNullOrWhiteSpace(WifiCertificatePath))
            {
                return "Enterprise Wi-Fi requires a trusted root CA certificate file when certificate trust is enabled.";
            }
        }

        return string.Empty;
    }
    public sealed record SecurityTypeOption(string Value, string DisplayName);
}
