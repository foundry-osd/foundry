using CommunityToolkit.Mvvm.ComponentModel;
using Foundry.Models.Configuration;

namespace Foundry.ViewModels;

public partial class NetworkSettingsViewModel : ObservableObject
{
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
}
