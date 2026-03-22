using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Foundry.Models.Configuration;
using Foundry.Services.ApplicationShell;
using Foundry.Services.Localization;
using Foundry.Services.Operations;

namespace Foundry.ViewModels;

public partial class NetworkSettingsViewModel : LocalizedViewModelBase
{
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
    }

    public IReadOnlyList<string> AvailableSecurityTypes { get; } =
    [
        "WPA2-Enterprise",
        "WPA2-Personal",
        "Open"
    ];

    [ObservableProperty]
    private bool isDot1xEnabled;

    [ObservableProperty]
    private string certificatePath = string.Empty;

    [ObservableProperty]
    private bool isWifiEnabled;

    [ObservableProperty]
    private string wifiSsid = string.Empty;

    [ObservableProperty]
    private string wifiSecurityType = "WPA2-Enterprise";

    [RelayCommand(CanExecute = nameof(CanBrowseCertificate))]
    private void BrowseCertificate()
    {
        string? selectedPath = _applicationShellService.PickOpenFilePath(
            Strings["Dot1xCertificatePickerTitle"],
            Strings["CertificatePickerFilter"]);
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            CertificatePath = selectedPath;
        }
    }

    public NetworkSettings BuildSettings()
    {
        return new NetworkSettings
        {
            Dot1x = new Dot1xSettings
            {
                IsEnabled = IsDot1xEnabled,
                CertificatePath = IsDot1xEnabled && !string.IsNullOrWhiteSpace(CertificatePath)
                    ? CertificatePath.Trim()
                    : null
            },
            Wifi = new WifiSettings
            {
                IsEnabled = IsWifiEnabled,
                Ssid = IsWifiEnabled && !string.IsNullOrWhiteSpace(WifiSsid)
                    ? WifiSsid.Trim()
                    : null,
                SecurityType = IsWifiEnabled && !string.IsNullOrWhiteSpace(WifiSecurityType)
                    ? WifiSecurityType.Trim()
                    : null
            }
        };
    }

    public void ApplySettings(NetworkSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        IsDot1xEnabled = settings.Dot1x.IsEnabled;
        CertificatePath = settings.Dot1x.CertificatePath ?? string.Empty;
        IsWifiEnabled = settings.Wifi.IsEnabled;
        WifiSsid = settings.Wifi.Ssid ?? string.Empty;
        WifiSecurityType = string.IsNullOrWhiteSpace(settings.Wifi.SecurityType)
            ? "WPA2-Enterprise"
            : settings.Wifi.SecurityType;
    }

    public override void Dispose()
    {
        _operationProgressService.ProgressChanged -= OnOperationProgressChanged;
        base.Dispose();
    }

    private bool CanBrowseCertificate()
    {
        return !_operationProgressService.IsOperationInProgress;
    }

    private void OnOperationProgressChanged(object? sender, EventArgs e)
    {
        RunOnUiThread(() => BrowseCertificateCommand.NotifyCanExecuteChanged());
    }
}
