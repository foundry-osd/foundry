// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using CommunityToolkit.Mvvm.ComponentModel;
using Foundry.Connect.Network;
using Foundry.Connect.Network.Policy;
using Foundry.Wpf.Localization;

namespace Foundry.Connect.ViewModels;

/// <summary>
/// One row of the Wi-Fi list, identified by its SSID bytes. The row is reused across observations so the list
/// selection keeps the same instance (T12).
/// </summary>
/// <remarks>
/// A row that is no longer visible keeps its last seen security and reports a signal of 0 (the selected network stays
/// listed as "Not in range", L3-9). Whether the network can be joined from the list comes from
/// <see cref="WifiSecurityClassifier.ToProfileSecurity"/> (L3-8). The row holds no secret; every member runs on the UI
/// thread.
/// </remarks>
public sealed class WifiNetworkItemViewModel : ObservableObject
{
    private const string WeakSignalGlyph = "";
    private const string FairSignalGlyph = "";
    private const string GoodSignalGlyph = "";
    private const string ExcellentSignalGlyph = "";

    private readonly ILocalizationService _localization;

    /// <summary>
    /// Creates a row that is not in range until the first <see cref="Update"/>.
    /// </summary>
    /// <param name="localization">Source of the texts.</param>
    /// <param name="ssid">The network SSID.</param>
    public WifiNetworkItemViewModel(ILocalizationService localization, WifiSsid ssid)
    {
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        Ssid = ssid ?? throw new ArgumentNullException(nameof(ssid));
    }

    /// <summary>
    /// Gets the SSID that identifies the row.
    /// </summary>
    public WifiSsid Ssid { get; }

    /// <summary>
    /// Gets the SSID as displayed, never trimmed.
    /// </summary>
    public string DisplayName => Ssid.DisplayName;

    /// <summary>
    /// Gets the last seen security type.
    /// </summary>
    public WifiSecurityType Security { get; private set; }

    /// <summary>
    /// Gets the security of the profile written to join the network, or <see langword="null"/> when it cannot be
    /// joined from the list.
    /// </summary>
    public WifiProfileSecurity? ProfileSecurity => WifiSecurityClassifier.ToProfileSecurity(Security);

    /// <summary>
    /// Gets whether the network can be joined from the list.
    /// </summary>
    public bool IsSupported => ProfileSecurity is not null;

    /// <summary>
    /// Gets whether the network uses 802.1X; it can only be joined through a provisioned profile.
    /// </summary>
    public bool IsEnterprise => Security is WifiSecurityType.WpaEnterprise or WifiSecurityType.Wpa2Enterprise or WifiSecurityType.Wpa3Enterprise;

    /// <summary>
    /// Gets whether joining the network needs a passphrase.
    /// </summary>
    public bool RequiresPassphrase => ProfileSecurity is WifiProfileSecurity.Wpa2Personal or WifiProfileSecurity.Wpa3Personal or WifiProfileSecurity.Wpa2Wpa3Personal;

    /// <summary>
    /// Gets the security label.
    /// </summary>
    public string SecurityText => _localization.GetString(NetworkTextKeys.SecurityKey(Security));

    /// <summary>
    /// Gets the signal quality from 0 to 100; 0 when the network is not in range.
    /// </summary>
    public int SignalQuality { get; private set; }

    /// <summary>
    /// Gets the Segoe Fluent signal-strength glyph.
    /// </summary>
    public string SignalGlyph => SignalQuality switch
    {
        <= 25 => WeakSignalGlyph,
        <= 50 => FairSignalGlyph,
        <= 75 => GoodSignalGlyph,
        _ => ExcellentSignalGlyph
    };

    /// <summary>
    /// Gets whether the last observation listed the network.
    /// </summary>
    public bool IsInRange { get; private set; }

    /// <summary>
    /// Gets whether the current Wi-Fi connection uses this SSID.
    /// </summary>
    public bool IsConnected { get; private set; }

    /// <summary>
    /// Gets the name announced by screen readers for the row.
    /// </summary>
    public string AutomationName => _localization.Format(
        IsConnected ? "Wifi.NetworkConnectedAutomationFormat" : "Wifi.NetworkAutomationFormat",
        DisplayName,
        SecurityText,
        SignalQuality);

    /// <summary>
    /// Applies the latest observation of the network.
    /// </summary>
    /// <param name="network">The visible network, or <see langword="null"/> when it is not in range (the security is kept).</param>
    /// <param name="isConnected">Whether the current connection uses this SSID.</param>
    public void Update(VisibleWifiNetwork? network, bool isConnected)
    {
        if (network is not null)
        {
            Security = network.Security;
            SignalQuality = network.SignalQuality;
            IsInRange = true;
        }
        else
        {
            SignalQuality = 0;
            IsInRange = false;
        }

        IsConnected = isConnected;
        OnPropertyChanged(nameof(Security));
        OnPropertyChanged(nameof(ProfileSecurity));
        OnPropertyChanged(nameof(IsSupported));
        OnPropertyChanged(nameof(IsEnterprise));
        OnPropertyChanged(nameof(RequiresPassphrase));
        OnPropertyChanged(nameof(SignalQuality));
        OnPropertyChanged(nameof(SignalGlyph));
        OnPropertyChanged(nameof(IsInRange));
        OnPropertyChanged(nameof(IsConnected));
        RefreshTexts();
    }

    /// <summary>
    /// Raises the texts again after a language change.
    /// </summary>
    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(SecurityText));
        OnPropertyChanged(nameof(AutomationName));
    }
}
