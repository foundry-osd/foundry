// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Network;
using Foundry.Connect.Security;
using Microsoft.Extensions.Logging;

namespace Foundry.Connect.ViewModels;

/// <summary>
/// The page's only path to the coordinator and to the deployment password prompt. Every technician action first reports a
/// networking interaction, which pauses the countdown; while a debug scenario is simulated every action is skipped and returns
/// NoChange.
/// </summary>
/// <remarks>
/// <para>
/// Coordinator operations already run their blocking work off the caller thread and never throw on cancellation, so
/// callers await them directly. The skip log carries the operation name only, never the SSID or the passphrase.
/// </para>
/// <para>
/// The two unlock entry points, <see cref="UnlockWiredAtStartupAsync"/> and <see cref="UnlockProvisionedWifiAsync"/>, are
/// raised by Connect, not by the technician, but a prompt counts as a networking interaction once it is shown: the
/// interaction is reported right before the prompt opens, so the countdown never completes and Connect never exits while it
/// is open, and focus does not move afterwards. They throw only <see cref="OperationCanceledException"/>. The first prompt
/// may run synchronously inside the call (a nested modal loop on the UI thread); the provisioned Wi-Fi is refreshed only
/// after the unlock has completed.
/// </para>
/// </remarks>
/// <param name="coordinator">Coordinator that executes the live operations.</param>
/// <param name="unlock">Deployment password prompt that unlocks the protected network secrets.</param>
/// <param name="logger">Logger for skipped simulated actions.</param>
public sealed class ConnectNetworkActions(IConnectionCoordinator coordinator, IConnectUnlockService unlock, ILogger<ConnectNetworkActions> logger)
{
    private readonly IConnectionCoordinator _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
    private readonly IConnectUnlockService _unlock = unlock ?? throw new ArgumentNullException(nameof(unlock));
    private readonly ILogger<ConnectNetworkActions> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Gets or sets whether a debug scenario is simulated; while set, no action reaches the coordinator.
    /// </summary>
    public bool IsSimulation { get; set; }

    /// <summary>
    /// Raised on the calling thread before every technician action, before every unlock prompt, and by
    /// <see cref="NotifyInteraction"/>.
    /// </summary>
    public event EventHandler? InteractionOccurred;

    /// <summary>
    /// Reports a networking interaction that is not an operation, such as selecting a Wi-Fi network or editing its password.
    /// </summary>
    public void NotifyInteraction() => InteractionOccurred?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Connects the provisioned Wi-Fi profile. A profile locked by the deployment password is unlocked first; when the
    /// password is not entered, nothing is connected. When a concurrent automatic unlock already succeeded, the profile is
    /// refreshed and connected without a prompt.
    /// </summary>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>
    /// The coordinator outcome; <see cref="OperationStatus.Cancelled"/> when the prompt was cancelled or the caller cancelled
    /// during the unlock; <see cref="OperationStatus.NoChange"/> while simulated.
    /// </returns>
    public Task<OperationOutcome> ConnectProvisionedWifiAsync(CancellationToken cancellationToken) =>
        Run(NetworkOperationKind.ConnectProvisionedWifi, () => UnlockThenConnectProvisionedWifiAsync(cancellationToken));

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

    /// <summary>
    /// Asks for the deployment password at startup when wired 802.1X needs a locked secret. Call it before the
    /// provisioning, once the main window is shown; a cancel leaves wired 802.1X unavailable for the session.
    /// </summary>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns><see cref="ConnectUnlockResult.NotRequired"/> without a prompt unless a wired secret is locked; else the unlock outcome.</returns>
    /// <exception cref="OperationCanceledException">The caller cancelled before the password was accepted.</exception>
    public Task<ConnectUnlockResult> UnlockWiredAtStartupAsync(CancellationToken cancellationToken)
    {
        if (!_unlock.IsWiredUnlockRequired)
        {
            return Task.FromResult(ConnectUnlockResult.NotRequired);
        }

        NotifyInteraction();
        return _unlock.UnlockAsync(ConnectUnlockTrigger.WiredStartup, cancellationToken);
    }

    /// <summary>
    /// Asks for the deployment password because Ethernet gives no internet and the locked provisioned Wi-Fi is needed
    /// (<see cref="IConnectionCoordinator.UnlockRequested"/>), then refreshes the provisioned Wi-Fi so the page shows
    /// it ready, or locked with the "not entered" text after a cancel.
    /// </summary>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>
    /// <see cref="ConnectUnlockResult.NotRequired"/> without a prompt while simulated or when no Wi-Fi secret is locked;
    /// else the unlock outcome.
    /// </returns>
    /// <exception cref="OperationCanceledException">The caller cancelled before the unlock or the refresh completed.</exception>
    public Task<ConnectUnlockResult> UnlockProvisionedWifiAsync(CancellationToken cancellationToken)
    {
        if (IsSimulation || !_unlock.IsWifiUnlockRequired)
        {
            return Task.FromResult(ConnectUnlockResult.NotRequired);
        }

        NotifyInteraction();
        return UnlockAndRefreshProvisionedWifiAsync(ConnectUnlockTrigger.AutomaticWifi, cancellationToken);
    }

    private async Task<OperationOutcome> UnlockThenConnectProvisionedWifiAsync(CancellationToken cancellationToken)
    {
        if (_coordinator.State.Wifi.Provisioned.Status == ProvisionedWifiStatus.Locked)
        {
            ConnectUnlockResult result;
            try
            {
                // No ConfigureAwait(false): the command that awaits this updates bound properties on the UI thread.
                result = await UnlockAndRefreshProvisionedWifiAsync(ConnectUnlockTrigger.ManualWifi, cancellationToken);
                if (result == ConnectUnlockResult.NotRequired)
                {
                    // An automatic unlock succeeded while this call waited for the prompt: its refresh may not have run yet,
                    // so build the now readable profile before connecting it.
                    await _coordinator.RefreshProvisionedWifiAsync(cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Manual operations report a cancelled caller as an outcome, never as an exception.
                return OperationOutcome.Cancelled(NetworkOperationKind.ConnectProvisionedWifi);
            }

            if (result == ConnectUnlockResult.Cancelled)
            {
                return OperationOutcome.Cancelled(NetworkOperationKind.ConnectProvisionedWifi);
            }
        }

        return await _coordinator.ConnectProvisionedWifiAsync(cancellationToken);
    }

    // The refresh runs only once the unlock completed: the secrets are decrypted one item at a time. NotRequired is not
    // refreshed here: this call unlocked nothing, and a caller that needs the profile of an unlock that completed meanwhile
    // refreshes it itself.
    private async Task<ConnectUnlockResult> UnlockAndRefreshProvisionedWifiAsync(ConnectUnlockTrigger trigger, CancellationToken cancellationToken)
    {
        ConnectUnlockResult result = await _unlock.UnlockAsync(trigger, cancellationToken);
        if (result != ConnectUnlockResult.NotRequired)
        {
            await _coordinator.RefreshProvisionedWifiAsync(cancellationToken);
        }

        return result;
    }

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
