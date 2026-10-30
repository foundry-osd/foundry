// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Network;
using Foundry.Connect.Network.Policy;

namespace Foundry.Connect.ViewModels;

/// <summary>
/// Maps typed codes and states to Connect resource keys. It is the only place where network codes become text keys:
/// the coordinator and the policies stay free of user-facing text, and view models resolve the key through the
/// localization service when the language changes.
/// </summary>
public static class NetworkTextKeys
{
    /// <summary>
    /// Gets the message key for a failed operation, choosing the most specific text for its failure code.
    /// </summary>
    /// <param name="outcome">The operation outcome.</param>
    /// <param name="provisionedPanel">Whether the message is shown in the provisioned Wi-Fi card, which words disconnect failures differently.</param>
    /// <returns>
    /// The resource key, or <see langword="null"/> unless the outcome is <see cref="OperationStatus.Failed"/>. A lost
    /// adapter (<see cref="NetworkFailureCode.NoWirelessAdapter"/>) or Wi-Fi runtime
    /// (<see cref="NetworkFailureCode.WifiRuntimeMissing"/>) also gives <see langword="null"/>: the Wi-Fi card shows that
    /// state once, without a feedback line.
    /// </returns>
    public static string? OutcomeKey(OperationOutcome outcome, bool provisionedPanel = false)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (outcome.Status != OperationStatus.Failed)
        {
            return null;
        }

        return outcome.Failure switch
        {
            NetworkFailureCode.WifiRuntimeMissing or NetworkFailureCode.NoWirelessAdapter => null,
            NetworkFailureCode.WifiFeatureDisabled or NetworkFailureCode.ProvisionedProfileUnavailable => "Wifi.ActionProvisionedProfileUnavailable",
            NetworkFailureCode.WifiSecretUnavailable => "Wifi.ProvisionedUnavailableSecret",
            NetworkFailureCode.WifiSecretLocked => "Wifi.ProvisionedLocked",
            NetworkFailureCode.InvalidPassphrase => "Wifi.ActionInvalidPassphrase",
            NetworkFailureCode.NetworkNotVisible => "Wifi.ActionNetworkNotVisible",
            NetworkFailureCode.UnsupportedSecurity => "Wifi.ActionSelectedNotSupported",
            NetworkFailureCode.ConnectTimedOut => "Wifi.ActionConnectTimedOut",
            NetworkFailureCode.WifiProfileRejected or NetworkFailureCode.ConnectRequestFailed or NetworkFailureCode.ConnectionFailed =>
                outcome.Operation == NetworkOperationKind.ConnectProvisionedWifi ? "Wifi.ActionProvisionedConnectFailed" : "Wifi.ActionConnectFailed",
            NetworkFailureCode.DisconnectFailed or NetworkFailureCode.DisconnectTimedOut =>
                provisionedPanel ? "Wifi.ActionProvisionedDisconnectFailed" : "Wifi.ActionDisconnectFailed",
            NetworkFailureCode.WifiCertificateImportFailed => "Wifi.ActionApplyProvisionedFailed",
            NetworkFailureCode.WiredSecretUnavailable => "Ethernet.Dot1xReasonSecretUnavailable",
            NetworkFailureCode.WiredProfileTemplateMissing => "Ethernet.Dot1xReasonProfileTemplateMissing",
            _ => "Wifi.ActionTryAgain"
        };
    }

    /// <summary>
    /// Gets the label key for a wired 802.1X authentication status.
    /// </summary>
    /// <param name="status">The authentication status.</param>
    /// <returns>The resource key, or <see langword="null"/> when no wired profile is configured.</returns>
    public static string? Dot1xStatusKey(Dot1xAuthenticationStatus status) => status switch
    {
        Dot1xAuthenticationStatus.Unavailable => "Ethernet.Dot1xUnavailable",
        Dot1xAuthenticationStatus.ProfileApplied => "Ethernet.Dot1xProfileApplied",
        Dot1xAuthenticationStatus.Authenticating => "Ethernet.Dot1xAuthenticating",
        Dot1xAuthenticationStatus.Authenticated => "Ethernet.Dot1xAuthenticated",
        Dot1xAuthenticationStatus.Failed => "Ethernet.Dot1xFailed",
        _ => null
    };

    /// <summary>
    /// Gets the explanation key for the reason attached to an unavailable or failed wired 802.1X state.
    /// </summary>
    /// <param name="reason">The failure reason.</param>
    /// <returns>The resource key, or <see langword="null"/> when there is no failure.</returns>
    public static string? Dot1xReasonKey(Dot1xFailureReason reason) => reason switch
    {
        Dot1xFailureReason.SecretUnavailable => "Ethernet.Dot1xReasonSecretUnavailable",
        Dot1xFailureReason.ProfileTemplateMissing => "Ethernet.Dot1xReasonProfileTemplateMissing",
        Dot1xFailureReason.ProfileImportFailed => "Ethernet.Dot1xReasonProfileImportFailed",
        Dot1xFailureReason.AuthenticationFailed => "Ethernet.Dot1xReasonAuthenticationFailed",
        Dot1xFailureReason.UnlockCancelled => "Ethernet.Dot1xReasonUnlockCancelled",
        _ => null
    };

    /// <summary>
    /// Gets the explanation key for a provisioned Wi-Fi profile that cannot be used.
    /// </summary>
    /// <param name="reason">Why the profile is unavailable.</param>
    /// <returns>The resource key; an unrecognized reason maps to the generic unavailable text.</returns>
    public static string ProvisionedUnavailableKey(ProvisionedWifiUnavailableReason reason) => reason switch
    {
        ProvisionedWifiUnavailableReason.SecretUnavailable => "Wifi.ProvisionedUnavailableSecret",
        ProvisionedWifiUnavailableReason.ProfileTemplateMissing => "Wifi.ProvisionedUnavailableTemplate",
        ProvisionedWifiUnavailableReason.InvalidConfiguration => "Wifi.ProvisionedUnavailableConfiguration",
        ProvisionedWifiUnavailableReason.InvalidPassphrase => "Wifi.ProvisionedUnavailablePassphrase",
        ProvisionedWifiUnavailableReason.UnlockCancelled => "Wifi.ProvisionedUnlockCancelled",
        _ => "Wifi.ActionProvisionedProfileUnavailable"
    };

    /// <summary>
    /// Gets the label key for the security type of a visible network or the current connection.
    /// </summary>
    /// <param name="security">The security type.</param>
    /// <returns>The resource key; unknown types map to the vendor-specific label.</returns>
    public static string SecurityKey(WifiSecurityType security) => security switch
    {
        WifiSecurityType.Open => "Wifi.SecurityOpen",
        WifiSecurityType.Owe => "Wifi.SecurityOwe",
        WifiSecurityType.Wep => "Wifi.SecurityWep",
        WifiSecurityType.WpaPersonal => "Wifi.SecurityWpaPersonal",
        WifiSecurityType.Wpa2Personal => "Wifi.SecurityWpa2Personal",
        WifiSecurityType.Wpa3Personal => "Wifi.SecurityWpa3Personal",
        WifiSecurityType.WpaEnterprise => "Wifi.SecurityWpaEnterprise",
        WifiSecurityType.Wpa2Enterprise => "Wifi.SecurityWpa2Enterprise",
        WifiSecurityType.Wpa3Enterprise => "Wifi.SecurityWpa3Enterprise",
        _ => "Wifi.SecurityVendorSpecific"
    };

    /// <summary>
    /// Gets the label key for the security of the provisioned profile.
    /// </summary>
    /// <param name="security">The profile security, or <see langword="null"/> when it is not known (the profile is a template).</param>
    /// <returns>The resource key; an unknown security maps to the generic configured-profile label.</returns>
    public static string ProfileSecurityKey(WifiProfileSecurity? security) => security switch
    {
        WifiProfileSecurity.Open => "Wifi.SecurityOpen",
        WifiProfileSecurity.Owe => "Wifi.SecurityOwe",
        WifiProfileSecurity.Wpa2Personal => "Wifi.SecurityWpa2Personal",
        WifiProfileSecurity.Wpa3Personal => "Wifi.SecurityWpa3Personal",
        WifiProfileSecurity.Wpa2Wpa3Personal => "Wifi.SecurityWpa2Wpa3Personal",
        _ => "Wifi.ConfiguredProfile"
    };

    /// <summary>
    /// Gets the key of the one-line Wi-Fi notice that replaces the Wi-Fi cards when the WLAN stack cannot work.
    /// </summary>
    /// <param name="availability">The Wi-Fi availability.</param>
    /// <returns>
    /// The resource key for a missing runtime or a missing adapter; <see langword="null"/> when Wi-Fi works or is not
    /// enabled for this media, which shows nothing Wi-Fi related.
    /// </returns>
    public static string? WifiNoticeKey(WifiAvailability availability) => availability switch
    {
        WifiAvailability.RuntimeMissing => "Wifi.UnavailableAtRuntime",
        WifiAvailability.NoAdapter => "Wifi.NoAdapterDetected",
        _ => null
    };

    /// <summary>
    /// Gets the hero description key for a blocked page. It offers only what this device can do: without the Wi-Fi
    /// section no text names Wi-Fi, and without an Ethernet adapter no text proposes Ethernet. A missing adapter points
    /// to the sections that are shown.
    /// </summary>
    /// <param name="reason">The blocked reason.</param>
    /// <param name="hasEthernetAdapter">Whether an Ethernet adapter is seen.</param>
    /// <param name="wifi">The current Wi-Fi availability.</param>
    /// <param name="isWifiSectionShown">Whether the page shows the Wi-Fi section.</param>
    /// <returns>The resource key; any other reason maps to the generic no-internet text.</returns>
    public static string BlockedDescriptionKey(BlockedReason reason, bool hasEthernetAdapter, WifiAvailability wifi, bool isWifiSectionShown) => reason switch
    {
        BlockedReason.NoAdapter => NoAdapterDescriptionKey(isWifiSectionShown),
        BlockedReason.NoLink when hasEthernetAdapter && wifi == WifiAvailability.Available => "Status.BlockedNoLinkDescription",
        BlockedReason.NoLink when hasEthernetAdapter => "Status.BlockedNoCableDescription",
        BlockedReason.NoLink when wifi == WifiAvailability.Available => "Status.BlockedJoinWifiDescription",
        BlockedReason.NoLink => NoAdapterDescriptionKey(isWifiSectionShown),
        BlockedReason.NoIpAddress => "Status.BlockedNoIpAddressDescription",
        BlockedReason.WiredAuthentication => "Status.BlockedDot1xDescription",
        BlockedReason.CaptivePortal => "Status.BlockedCaptivePortalDescription",
        _ => "Status.BlockedNoInternetDescription"
    };

    /// <summary>
    /// Gets the title and description keys of the hero banner. Checking and ready have no description. Waiting names
    /// what runs: the provisioning, a network action (a Wi-Fi disconnect has its own title, never "Connecting"), or,
    /// with no operation, wired 802.1X authentication.
    /// </summary>
    /// <param name="status">The page status.</param>
    /// <param name="reason">The blocked reason; used only when the page is blocked.</param>
    /// <param name="state">The rendered network state; its running operation words the waiting texts.</param>
    /// <param name="isWifiSectionShown">Whether the page shows the Wi-Fi section; used only when the page is blocked.</param>
    /// <returns>The resource keys; the description key is <see langword="null"/> when the banner has no description.</returns>
    public static (string TitleKey, string? DescriptionKey) HeroKeys(ConnectPageStatus status, BlockedReason reason, NetworkState state, bool isWifiSectionShown)
    {
        ArgumentNullException.ThrowIfNull(state);
        return status switch
        {
            ConnectPageStatus.Checking => ("Status.CheckingTitle", null),
            ConnectPageStatus.Waiting => (WaitingTitleKey(state.ActiveOperation), WaitingDescriptionKey(state.ActiveOperation)),
            ConnectPageStatus.Ready => ("Status.NetworkReadyTitle", null),
            _ => ("Status.BlockedTitle", BlockedDescriptionKey(reason, state.Ethernet.HasAdapter, state.Wifi.Availability, isWifiSectionShown))
        };
    }

    private static string NoAdapterDescriptionKey(bool isWifiSectionShown) =>
        isWifiSectionShown ? "Status.BlockedNoAdapterDescription" : "Status.BlockedNoEthernetAdapterDescription";

    private static string WaitingTitleKey(NetworkOperationKind? operation) =>
        operation == NetworkOperationKind.DisconnectWifi ? "Status.DisconnectingTitle" : "Status.ConnectingTitle";

    private static string WaitingDescriptionKey(NetworkOperationKind? operation) => operation switch
    {
        NetworkOperationKind.ApplyProvisioning => "Status.ApplyingProvisioningDescription",
        null => "Status.Dot1xAuthenticatingDescription",
        _ => "Status.ConnectingDescription"
    };
}
