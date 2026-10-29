// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Connect.Network;

/// <summary>
/// Identifies a network operation run by the connection coordinator.
/// </summary>
public enum NetworkOperationKind
{
    /// <summary>Applies the provisioned wired and Wi-Fi settings at startup.</summary>
    ApplyProvisioning,

    /// <summary>Connects the provisioned Wi-Fi profile.</summary>
    ConnectProvisionedWifi,

    /// <summary>Connects a Wi-Fi network chosen by the technician.</summary>
    ConnectWifiNetwork,

    /// <summary>Disconnects the current Wi-Fi connection.</summary>
    DisconnectWifi
}

/// <summary>
/// Terminal status of a network operation.
/// </summary>
public enum OperationStatus
{
    /// <summary>The operation changed the network configuration or connection as requested.</summary>
    Succeeded,

    /// <summary>The requested state already existed, so nothing was changed.</summary>
    NoChange,

    /// <summary>The operation failed; see <see cref="OperationOutcome.Failure"/>.</summary>
    Failed,

    /// <summary>The operation was cancelled before completion.</summary>
    Cancelled
}

/// <summary>
/// Typed reason for a failed network operation. It carries no localized text and no network identifier: the adapter
/// maps it to a user-facing message and <see cref="NetworkFailureTaxonomy"/> maps it to remote diagnostic fields.
/// </summary>
public enum NetworkFailureCode
{
    /// <summary>No failure.</summary>
    None,

    /// <summary>An unclassified failure.</summary>
    Unexpected,

    /// <summary>The Wi-Fi feature is not enabled for this media.</summary>
    WifiFeatureDisabled,

    /// <summary>The WLAN runtime (service or <c>wlanapi.dll</c>) is not available.</summary>
    WifiRuntimeMissing,

    /// <summary>No wireless adapter is present.</summary>
    NoWirelessAdapter,

    /// <summary>The provisioned Wi-Fi profile is not usable.</summary>
    ProvisionedProfileUnavailable,

    /// <summary>The provisioned Wi-Fi secret could not be read.</summary>
    WifiSecretUnavailable,

    /// <summary>The passphrase or key does not satisfy the security type.</summary>
    InvalidPassphrase,

    /// <summary>The selected network is not in the visible list.</summary>
    NetworkNotVisible,

    /// <summary>The network security type is not supported for a manual connection.</summary>
    UnsupportedSecurity,

    /// <summary>Windows rejected the Wi-Fi profile.</summary>
    WifiProfileRejected,

    /// <summary>The connect request was refused by the WLAN service.</summary>
    ConnectRequestFailed,

    /// <summary>The connection attempt completed with a failure; the WLAN reason code may be set.</summary>
    ConnectionFailed,

    /// <summary>The connection did not complete in time.</summary>
    ConnectTimedOut,

    /// <summary>The disconnect request was refused by the WLAN service.</summary>
    DisconnectFailed,

    /// <summary>The disconnect did not complete in time.</summary>
    DisconnectTimedOut,

    /// <summary>The Wi-Fi 802.1X certificate could not be imported.</summary>
    WifiCertificateImportFailed,

    /// <summary>The wired 802.1X profile template is missing.</summary>
    WiredProfileTemplateMissing,

    /// <summary>The wired 802.1X secret could not be read.</summary>
    WiredSecretUnavailable,

    /// <summary>The wired 802.1X certificate could not be imported.</summary>
    WiredCertificateImportFailed,

    /// <summary>Windows rejected the wired 802.1X profile.</summary>
    WiredProfileImportFailed,

    /// <summary>The wired adapter could not be reconnected after the profile was applied.</summary>
    WiredReconnectFailed
}

/// <summary>
/// Result of one network operation.
/// </summary>
/// <param name="Operation">Operation that produced the outcome.</param>
/// <param name="Status">Terminal status.</param>
/// <param name="Failure">Failure code; <see cref="NetworkFailureCode.None"/> unless <paramref name="Status"/> is <see cref="OperationStatus.Failed"/>.</param>
/// <param name="WlanReasonCode">Native WLAN reason code when the failure came from a WLAN notification or profile rejection.</param>
public sealed record OperationOutcome(
    NetworkOperationKind Operation,
    OperationStatus Status,
    NetworkFailureCode Failure = NetworkFailureCode.None,
    uint? WlanReasonCode = null)
{
    /// <summary>
    /// Gets a value indicating whether the operation reached the requested state, including when nothing had to change.
    /// </summary>
    public bool IsSuccess => Status is OperationStatus.Succeeded or OperationStatus.NoChange;

    /// <summary>
    /// Creates a successful outcome.
    /// </summary>
    /// <param name="operation">Operation that succeeded.</param>
    /// <returns>The outcome.</returns>
    public static OperationOutcome Success(NetworkOperationKind operation) => new(operation, OperationStatus.Succeeded);

    /// <summary>
    /// Creates an outcome for a request whose target state already existed.
    /// </summary>
    /// <param name="operation">Operation that had nothing to change.</param>
    /// <returns>The outcome.</returns>
    public static OperationOutcome NoChange(NetworkOperationKind operation) => new(operation, OperationStatus.NoChange);

    /// <summary>
    /// Creates a cancelled outcome.
    /// </summary>
    /// <param name="operation">Operation that was cancelled.</param>
    /// <returns>The outcome.</returns>
    public static OperationOutcome Cancelled(NetworkOperationKind operation) => new(operation, OperationStatus.Cancelled);

    /// <summary>
    /// Creates a failed outcome.
    /// </summary>
    /// <param name="operation">Operation that failed.</param>
    /// <param name="failure">Typed failure reason.</param>
    /// <param name="wlanReasonCode">Native WLAN reason code, when known.</param>
    /// <returns>The outcome.</returns>
    public static OperationOutcome Failed(NetworkOperationKind operation, NetworkFailureCode failure, uint? wlanReasonCode = null) =>
        new(operation, OperationStatus.Failed, failure, wlanReasonCode);
}
