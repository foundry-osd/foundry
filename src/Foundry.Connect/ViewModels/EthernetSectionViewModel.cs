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
/// The Ethernet section: a compact header (glyph, status pill, one secondary line and the wired 802.1X row with its
/// reason), the folded details (adapter, IPv4 address, gateway, IP assignment) and, without an adapter, the card with
/// the neutral "not available on this device" text.
/// </summary>
/// <remarks>
/// Every text is derived from the last <see cref="EthernetState"/> given to <see cref="Apply"/> and resolved on demand,
/// so <see cref="RefreshTexts"/> after a language change needs no network refresh. The details start collapsed and
/// only the technician opens or closes them: <see cref="Apply"/> never touches <see cref="IsDetailsExpanded"/>. The
/// "not available" card shows in every layout once the first observation exists. The view model reads no file and
/// does no I/O, and every member runs on the UI thread.
/// </remarks>
/// <param name="localization">Source of the texts.</param>
public sealed partial class EthernetSectionViewModel(ILocalizationService localization) : LocalizedViewModelBase(localization)
{
    private const string ConnectedGlyph = "\uE839";
    private const string WarningGlyph = "\uEB56";
    private const string ErrorGlyph = "\uEB55";

    private EthernetState _ethernet = EthernetState.Empty;
    private bool _isChecking = true;

    /// <summary>
    /// Gets or sets whether the details are expanded. False at start; only the technician changes it.
    /// </summary>
    [ObservableProperty]
    private bool isDetailsExpanded;

    /// <summary>
    /// Gets whether the card is shown: an Ethernet adapter exists.
    /// </summary>
    public bool ShowCard => _ethernet.HasAdapter;

    /// <summary>
    /// Gets whether the card with the "not available on this device" text is shown: an observation exists and no Ethernet
    /// adapter is seen (a missing adapter or a failed Ethernet read). It stays hidden before the first observation.
    /// </summary>
    public bool ShowNoAdapterNotice => !_isChecking && !_ethernet.HasAdapter;

    /// <summary>
    /// Gets the neutral "not available on this device" text.
    /// </summary>
    public string NoAdapterText => GetString("Ethernet.UnavailableOnDevice");

    /// <summary>
    /// Gets whether an interface has a link and a DHCP or static address.
    /// </summary>
    public bool IsConnected => _ethernet.HasUsableAddress;

    /// <summary>
    /// Gets the Segoe Fluent glyph: connected, adapter without usable address, or no adapter.
    /// </summary>
    public string Glyph => _ethernet.HasUsableAddress ? ConnectedGlyph : _ethernet.HasAdapter ? WarningGlyph : ErrorGlyph;

    /// <summary>
    /// Gets the status pill: no link, connected or waiting for the configuration.
    /// </summary>
    public string StatusText => GetString(_ethernet.Primary switch
    {
        null => "Ethernet.UnavailableOnDevice",
        { HasLink: false } => "Ethernet.NoActiveLink",
        { HasUsableAddress: true } => "Common.Connected",
        _ => "Ethernet.WaitingConfiguration"
    });

    /// <summary>
    /// Gets the secondary header line: the IPv4 address when connected, "Self-assigned address" for an APIPA address,
    /// else an empty string. The banner keeps the diagnosis. The address is displayed only, never logged.
    /// </summary>
    public string SecondaryStatusText => _ethernet.Primary switch
    {
        { HasUsableAddress: true, Ipv4Address: { } address } => address,
        { HasLink: true, IpConfiguration: IpConfigurationKind.Apipa } => GetString("Ethernet.ApipaAddress"),
        _ => string.Empty
    };

    /// <summary>
    /// Gets the configuration detail: a DHCP lease or a static configuration, else an empty string.
    /// </summary>
    public string ConfigurationText => _ethernet.Primary switch
    {
        { HasUsableAddress: true, IpConfiguration: IpConfigurationKind.Dhcp } => GetString("Ethernet.DhcpLeaseDetected"),
        { HasUsableAddress: true, IpConfiguration: IpConfigurationKind.Static } => GetString("Ethernet.StaticConfigurationDetected"),
        _ => string.Empty
    };

    /// <summary>
    /// Gets whether the details show the IP assignment row: <see cref="ConfigurationText"/> is not empty.
    /// </summary>
    public bool HasConfiguration => ConfigurationText.Length > 0;

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
    /// Gets the severity glyph of the 802.1X row: success when authenticated, error when failed, warning when
    /// unavailable, else informational.
    /// </summary>
    public InfoBarSeverity Dot1xSeverity => _ethernet.Dot1x.Status switch
    {
        Dot1xAuthenticationStatus.Authenticated => InfoBarSeverity.Success,
        Dot1xAuthenticationStatus.Failed => InfoBarSeverity.Error,
        Dot1xAuthenticationStatus.Unavailable => InfoBarSeverity.Warning,
        _ => InfoBarSeverity.Informational
    };

    /// <summary>
    /// Gets whether 802.1X is authenticating; the row then shows a progress ring instead of the severity glyph.
    /// </summary>
    public bool IsDot1xAuthenticating => _ethernet.Dot1x.Status == Dot1xAuthenticationStatus.Authenticating;

    /// <summary>
    /// Gets whether the 802.1X reason line is shown: the status is failed or unavailable.
    /// </summary>
    public bool HasDot1xReason => _ethernet.Dot1x.Status is Dot1xAuthenticationStatus.Failed or Dot1xAuthenticationStatus.Unavailable;

    /// <summary>
    /// Gets the reason of an unavailable or failed 802.1X state, or an empty string.
    /// </summary>
    public string Dot1xReasonText => NetworkTextKeys.Dot1xReasonKey(_ethernet.Dot1x.Reason) is { } key ? GetString(key) : string.Empty;

    /// <summary>
    /// Applies the latest wired state. The details expander is never changed here.
    /// </summary>
    /// <param name="ethernet">The wired state read from the coordinator.</param>
    /// <param name="isChecking">Whether no observation exists yet.</param>
    public void Apply(EthernetState ethernet, bool isChecking)
    {
        ArgumentNullException.ThrowIfNull(ethernet);

        _ethernet = ethernet;
        _isChecking = isChecking;
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
        OnPropertyChanged(nameof(ConfigurationText));
        OnPropertyChanged(nameof(HasConfiguration));
        OnPropertyChanged(nameof(AdapterName));
        OnPropertyChanged(nameof(IpAddress));
        OnPropertyChanged(nameof(Gateway));
        OnPropertyChanged(nameof(HasDot1x));
        OnPropertyChanged(nameof(Dot1xStatusText));
        OnPropertyChanged(nameof(Dot1xSeverity));
        OnPropertyChanged(nameof(IsDot1xAuthenticating));
        OnPropertyChanged(nameof(HasDot1xReason));
        OnPropertyChanged(nameof(Dot1xReasonText));
    }
}
