// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Network;
using Microsoft.Extensions.Logging;

namespace Foundry.Connect.ViewModels;

/// <summary>
/// The page's only path to the coordinator. Every action first reports a networking interaction (D3 pause);
/// while a debug scenario is simulated every action is skipped and returns NoChange (D16, L3-6).
/// </summary>
/// <remarks>
/// Coordinator operations already run their blocking work off the caller thread and never throw on cancellation, so
/// callers await them directly. The skip log carries the operation name only, never the SSID or the passphrase.
/// </remarks>
/// <param name="coordinator">Coordinator that executes the live operations.</param>
/// <param name="logger">Logger for skipped simulated actions.</param>
public sealed class ConnectNetworkActions(IConnectionCoordinator coordinator, ILogger<ConnectNetworkActions> logger)
{
    private readonly IConnectionCoordinator _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
    private readonly ILogger<ConnectNetworkActions> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Gets or sets whether a debug scenario is simulated; while set, no action reaches the coordinator.
    /// </summary>
    public bool IsSimulation { get; set; }

    /// <summary>
    /// Raised on the calling thread before every action, and by <see cref="NotifyInteraction"/>.
    /// </summary>
    public event EventHandler? InteractionOccurred;

    /// <summary>
    /// Reports a networking interaction that is not an operation, such as selecting a Wi-Fi network or editing its password.
    /// </summary>
    public void NotifyInteraction() => InteractionOccurred?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Connects the provisioned Wi-Fi profile.
    /// </summary>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>The coordinator outcome, or <see cref="OperationStatus.NoChange"/> while simulated.</returns>
    public Task<OperationOutcome> ConnectProvisionedWifiAsync(CancellationToken cancellationToken) =>
        Run(NetworkOperationKind.ConnectProvisionedWifi, () => _coordinator.ConnectProvisionedWifiAsync(cancellationToken));

    /// <summary>
    /// Connects a Wi-Fi network picked by the technician.
    /// </summary>
    /// <param name="ssid">The network to join.</param>
    /// <param name="passphrase">The passphrase as typed, or <see langword="null"/> for an open network.</param>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>The coordinator outcome, or <see cref="OperationStatus.NoChange"/> while simulated.</returns>
    public Task<OperationOutcome> ConnectWifiNetworkAsync(WifiSsid ssid, string? passphrase, CancellationToken cancellationToken) =>
        Run(NetworkOperationKind.ConnectWifiNetwork, () => _coordinator.ConnectWifiNetworkAsync(ssid, passphrase, cancellationToken));

    /// <summary>
    /// Disconnects the current Wi-Fi connection.
    /// </summary>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>The coordinator outcome, or <see cref="OperationStatus.NoChange"/> while simulated.</returns>
    public Task<OperationOutcome> DisconnectWifiAsync(CancellationToken cancellationToken) =>
        Run(NetworkOperationKind.DisconnectWifi, () => _coordinator.DisconnectWifiAsync(cancellationToken));

    private Task<OperationOutcome> Run(NetworkOperationKind kind, Func<Task<OperationOutcome>> operation)
    {
        NotifyInteraction();
        if (!IsSimulation)
        {
            return operation();
        }

        _logger.LogInformation(
            "Debug scenario active; network action skipped. NetworkOperation={NetworkOperation}",
            NetworkFailureTaxonomy.OperationName(kind));
        return Task.FromResult(OperationOutcome.NoChange(kind));
    }
}
