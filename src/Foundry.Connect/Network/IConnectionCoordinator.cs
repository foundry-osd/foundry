// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Connect.Network;

/// <summary>
/// The only component that changes the network configuration and owns the published <see cref="NetworkState"/>.
/// </summary>
/// <remarks>
/// Operations run one at a time. A manual request switches the mode to <see cref="ConnectionMode.Manual"/> and
/// publishes it before it waits for a running operation, so no further automatic attempt starts (D4, NET-01). Failures
/// are returned as typed <see cref="OperationOutcome"/> values; each operation is logged once at start and once at its
/// end (D17). The manual operations (<see cref="ConnectProvisionedWifiAsync"/>, <see cref="ConnectWifiNetworkAsync"/>,
/// <see cref="DisconnectWifiAsync"/>) never throw for a network or caller-cancellation reason: a cancelled request returns
/// <see cref="OperationStatus.Cancelled"/> and an unexpected error returns <see cref="OperationStatus.Failed"/> with
/// <see cref="NetworkFailureCode.Unexpected"/>. They throw only for invalid arguments (<see cref="ArgumentNullException"/>)
/// and after shutdown disposed the coordinator (<see cref="ObjectDisposedException"/>).
/// </remarks>
public interface IConnectionCoordinator
{
    /// <summary>
    /// Gets the last published state.
    /// </summary>
    NetworkState State { get; }

    /// <summary>
    /// Raised after each published state, outside any lock, on the thread that caused the change (often a thread-pool
    /// thread). Under concurrent publishes the argument may already be stale when the handler runs: handlers must read
    /// <see cref="State"/> again instead of trusting the argument. A handler that throws is logged and ignored.
    /// </summary>
    event EventHandler<NetworkState>? StateChanged;

    /// <summary>
    /// Runs the once-per-process provisioning under the operation gate, keeps its outcome and publishes the state.
    /// </summary>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>The provisioning outcome.</returns>
    /// <exception cref="OperationCanceledException">The caller cancelled.</exception>
    Task<ProvisioningOutcome> ApplyProvisioningAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Composes and publishes the state for a new observation, then runs at most one automatic provisioned attempt
    /// when the connection policy allows it (L2-20). No attempt starts while another operation runs.
    /// </summary>
    /// <param name="observation">What the monitor saw.</param>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>A task that completes after the state is published and any automatic attempt ended.</returns>
    Task ApplyObservationAsync(NetworkObservation observation, CancellationToken cancellationToken);

    /// <summary>
    /// Connects the provisioned Wi-Fi profile at the technician's request.
    /// </summary>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>The outcome.</returns>
    Task<OperationOutcome> ConnectProvisionedWifiAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Connects a visible network chosen by the technician. The passphrase is used only to build the profile and is
    /// never kept.
    /// </summary>
    /// <param name="ssid">SSID of a network in the current visible list.</param>
    /// <param name="passphrase">Passphrase or key for a personal network; ignored for open and OWE networks.</param>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>The outcome.</returns>
    Task<OperationOutcome> ConnectWifiNetworkAsync(WifiSsid ssid, string? passphrase, CancellationToken cancellationToken);

    /// <summary>
    /// Disconnects the current Wi-Fi connection at the technician's request.
    /// </summary>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>The outcome; <see cref="OperationStatus.NoChange"/> when nothing is connected.</returns>
    Task<OperationOutcome> DisconnectWifiAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Drops the provisioned profile document at exit (L2-6). Later provisioned connects report
    /// <see cref="NetworkFailureCode.ProvisionedProfileUnavailable"/>.
    /// </summary>
    void ReleaseProvisionedProfile();
}
