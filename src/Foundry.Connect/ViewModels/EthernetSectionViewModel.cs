// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using CommunityToolkit.Mvvm.ComponentModel;
using Foundry.Connect.Network;
using Foundry.Wpf.Controls;
using Foundry.Wpf.Localization;
using Foundry.Wpf.ViewModels;

namespace Foundry.Connect.ViewModels;

/// <summary>
/// The Ethernet card: link and address summary, the details (adapter, IP address, gateway) and the wired 802.1X row.
/// </summary>
/// <remarks>
/// Every text is derived from the last <see cref="EthernetState"/> given to <see cref="Apply"/> and resolved on demand,
/// so <see cref="RefreshTexts"/> after a language change needs no network refresh. The details expander is open
/// until the first usable address, then collapsed; afterwards it changes only when the usable state changes, so a
/// choice made by the technician survives the periodic observations. The view model reads no file and does
/// no I/O, and every member runs on the UI thread.
/// </remarks>
/// <param name="localization">Source of the texts.</param>
public sealed partial class EthernetSectionViewModel(ILocalizationService localization) : LocalizedViewModelBase(localization)
{
    private const string ConnectedGlyph = "\uE839";
    private const string WarningGlyph = "\uEB56";
    private const string ErrorGlyph = "\uEB55";

    private EthernetState _ethernet = EthernetState.Empty;
    private bool _isChecking = true;
    private bool _hasApplied;
    private bool _wasConnected;

    /// <summary>
    /// Gets or sets whether the details are expanded. The technician can change it; <see cref="Apply"/> overrides it
    /// only when <see cref="IsConnected"/> changes.
    /// </summary>
    [ObservableProperty]
    private bool isDetailsExpanded = true;

    /// <summary>
    /// Gets whether the card is shown: an Ethernet adapter exists.
    /// </summary>
    public bool ShowCard => _ethernet.HasAdapter;

    /// <summary>
    /// Gets whether the one-line "no adapter" notice is shown. It stays hidden while the first observation is pending.
    /// </summary>
    public bool ShowNoAdapterNotice => !_isChecking && !_ethernet.HasAdapter;

    /// <summary>
    /// Gets the text of the "no adapter" notice.
    /// </summary>
    public string NoAdapterText => GetString("Ethernet.NoAdapterDetected");

    /// <summary>
    /// Gets whether an interface has a link and a DHCP or static address.
    /// </summary>
    public bool IsConnected => _ethernet.HasUsableAddress;

    /// <summary>
    /// Gets the Segoe Fluent glyph: connected, adapter without usable address, or no adapter.
    /// </summary>
    public string Glyph => _ethernet.HasUsableAddress ? ConnectedGlyph : _ethernet.HasAdapter ? WarningGlyph : ErrorGlyph;

    /// <summary>
    /// Gets the headline status of the card.
    /// </summary>
    public string StatusText => GetString(StatusKeys.Primary);

    /// <summary>
    /// Gets the explanation under the headline, or an empty string.
    /// </summary>
    public string SecondaryStatusText => StatusKeys.Secondary is { } key ? GetString(key) : string.Empty;

    /// <summary>
    /// Gets the name of the described adapter.
    /// </summary>
    public string AdapterName => _ethernet.Primary?.Name ?? GetString("Common.Unavailable");

    /// <summary>
    /// Gets the IPv4 address of the described adapter. It is displayed only, never logged.
    /// </summary>
    public string IpAddress => _ethernet.Primary?.Ipv4Address ?? GetString("Common.Unavailable");

    /// <summary>
    /// Gets the IPv4 gateway of the described adapter.
    /// </summary>
    public string Gateway => _ethernet.Primary?.Gateway ?? GetString("Common.Unavailable");

    /// <summary>
    /// Gets whether a wired 802.1X profile is configured and its row is shown.
    /// </summary>
    public bool HasDot1x => _ethernet.Dot1x.Status != Dot1xAuthenticationStatus.NotConfigured;

    /// <summary>
    /// Gets the 802.1X status label, or an empty string when none is configured.
    /// </summary>
    public string Dot1xStatusText => NetworkTextKeys.Dot1xStatusKey(_ethernet.Dot1x.Status) is { } key ? GetString(key) : string.Empty;

    /// <summary>
    /// Gets the reason of an unavailable or failed 802.1X state, or an empty string.
    /// </summary>
    public string Dot1xReasonText => NetworkTextKeys.Dot1xReasonKey(_ethernet.Dot1x.Reason) is { } key ? GetString(key) : string.Empty;

    /// <summary>
    /// Gets whether the 802.1X InfoBar is shown: the status is failed or unavailable.
    /// </summary>
    public bool ShowDot1xNotice => _ethernet.Dot1x.Status is Dot1xAuthenticationStatus.Failed or Dot1xAuthenticationStatus.Unavailable;

    /// <summary>
    /// Gets the severity of the 802.1X InfoBar: error for a failure, warning for an unavailable profile.
    /// </summary>
    public InfoBarSeverity Dot1xNoticeSeverity => _ethernet.Dot1x.Status == Dot1xAuthenticationStatus.Failed ? InfoBarSeverity.Error : InfoBarSeverity.Warning;

    private (string Primary, string? Secondary) StatusKeys => _ethernet.Primary switch
    {
        null => ("Ethernet.NoAdapterDetected", null),
        { HasLink: false } => ("Ethernet.NoActiveLink", "Ethernet.CheckCable"),
        { IpConfiguration: IpConfigurationKind.Dhcp } => ("Common.Connected", "Ethernet.DhcpLeaseDetected"),
        { IpConfiguration: IpConfigurationKind.Static } => ("Common.Connected", "Ethernet.StaticConfigurationDetected"),
        { IpConfiguration: IpConfigurationKind.Apipa } => ("Ethernet.WaitingConfiguration", "Ethernet.ApipaAddress"),
        _ => ("Ethernet.WaitingConfiguration", "Ethernet.WaitingDhcp")
    };

    /// <summary>
    /// Applies the latest wired state. The first call sets the expander from the connected state; later calls change it
    /// only when the connected state changes.
    /// </summary>
    /// <param name="ethernet">The wired state read from the coordinator.</param>
    /// <param name="isChecking">Whether the first observation is still pending.</param>
    public void Apply(EthernetState ethernet, bool isChecking)
    {
        ArgumentNullException.ThrowIfNull(ethernet);

        _ethernet = ethernet;
        _isChecking = isChecking;

        bool connected = ethernet.HasUsableAddress;
        if (!_hasApplied || connected != _wasConnected)
        {
            IsDetailsExpanded = !connected;
        }

        _hasApplied = true;
        _wasConnected = connected;
        RaiseAll();
    }

    /// <summary>
    /// Raises every text again after a language change; the stored state is unchanged and the expander is untouched.
    /// </summary>
    public void RefreshTexts() => RaiseAll();

    private string GetString(string key) => LocalizationService.GetString(key);

    // Every property is computed from the stored state, so one notification per property is all a refresh needs.
    private void RaiseAll()
    {
        OnPropertyChanged(nameof(ShowCard));
        OnPropertyChanged(nameof(ShowNoAdapterNotice));
        OnPropertyChanged(nameof(NoAdapterText));
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(Glyph));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(SecondaryStatusText));
        OnPropertyChanged(nameof(AdapterName));
        OnPropertyChanged(nameof(IpAddress));
        OnPropertyChanged(nameof(Gateway));
        OnPropertyChanged(nameof(HasDot1x));
        OnPropertyChanged(nameof(Dot1xStatusText));
        OnPropertyChanged(nameof(Dot1xReasonText));
        OnPropertyChanged(nameof(ShowDot1xNotice));
        OnPropertyChanged(nameof(Dot1xNoticeSeverity));
    }
}
