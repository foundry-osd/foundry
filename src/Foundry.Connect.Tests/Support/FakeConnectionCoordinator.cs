// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Network;

namespace Foundry.Connect.Tests.Support;

/// <summary>
/// Scriptable <see cref="IConnectionCoordinator"/> for view-model tests. Connect and disconnect calls are counted and
/// return the next queued <see cref="OperationOutcome"/> (a success when the queue is empty); without a gate they
/// complete synchronously, never throw and ignore cancellation. <see cref="ApplyProvisioningAsync"/> and the connect and
/// disconnect calls can be held open through <see cref="ProvisioningGate"/> and <see cref="OperationGate"/> to test the
/// in-flight window.
/// </summary>
internal sealed class FakeConnectionCoordinator : IConnectionCoordinator
{
    /// <inheritdoc />
    public NetworkState State { get; set; } = NetworkState.Initial(new DateTimeOffset(2026, 10, 29, 8, 0, 0, TimeSpan.Zero));

    /// <inheritdoc />
    public event EventHandler<NetworkState>? StateChanged;

    /// <inheritdoc />
    public event EventHandler? UnlockRequested;

    /// <summary>Gets the number of <see cref="RefreshProvisionedWifiAsync"/> calls.</summary>
    public int RefreshProvisionedWifiCalls { get; private set; }

    /// <summary>Gets or sets the result of <see cref="ApplyProvisioningAsync"/> when no gate is set.</summary>
    public ProvisioningOutcome ProvisioningResult { get; set; } = ProvisioningOutcome.NotConfigured;

    /// <summary>
    /// Gets or sets a gate that holds <see cref="ApplyProvisioningAsync"/> open until the test completes it; the
    /// returned task honors the caller's cancellation.
    /// </summary>
    public TaskCompletionSource<ProvisioningOutcome>? ProvisioningGate { get; set; }

    /// <summary>
    /// Gets or sets whether the <see cref="ProvisioningGate"/> task ignores the caller's cancellation, so the outcome
    /// can arrive after the caller was cancelled.
    /// </summary>
    public bool ProvisioningIgnoresCancellation { get; set; }

    /// <summary>
    /// Gets or sets a gate whose task every connect or disconnect call returns after being recorded, so a test can act
    /// while the operation is in flight, then complete, fault or cancel it.
    /// </summary>
    public TaskCompletionSource<OperationOutcome>? OperationGate { get; set; }

    /// <summary>Gets the number of <see cref="ApplyProvisioningAsync"/> calls.</summary>
    public int ApplyProvisioningCalls { get; private set; }

    /// <summary>Gets the number of <see cref="ConnectProvisionedWifiAsync"/> calls.</summary>
    public int ConnectProvisionedCalls { get; private set; }

    /// <summary>Gets the arguments of every <see cref="ConnectWifiNetworkAsync"/> call.</summary>
    public List<(WifiSsid Ssid, string? Passphrase)> ConnectNetworkCalls { get; } = [];

    /// <summary>Gets the number of <see cref="DisconnectWifiAsync"/> calls.</summary>
    public int DisconnectCalls { get; private set; }

    /// <summary>Gets the number of <see cref="ReleaseProvisionedProfile"/> calls.</summary>
    public int ReleaseCalls { get; private set; }

    /// <summary>Gets the outcomes handed out, one per connect or disconnect call; an empty queue yields a success.</summary>
    public Queue<OperationOutcome> Outcomes { get; } = new();

    /// <summary>
    /// Makes <paramref name="state"/> the current state and raises <see cref="StateChanged"/> with it, on the calling thread.
    /// </summary>
    /// <param name="state">The new state.</param>
    public void Publish(NetworkState state)
    {
        State = state;
        StateChanged?.Invoke(this, state);
    }

    /// <summary>
    /// Raises <see cref="StateChanged"/> with <paramref name="staleArgument"/> without touching <see cref="State"/>,
    /// the race the coordinator contract allows: handlers must read <see cref="State"/> again instead of trusting the
    /// argument.
    /// </summary>
    /// <param name="staleArgument">An argument older than <see cref="State"/>.</param>
    public void RaiseStale(NetworkState staleArgument) => StateChanged?.Invoke(this, staleArgument);

    /// <summary>Raises <see cref="UnlockRequested"/> on the calling thread.</summary>
    public void RaiseUnlockRequested() => UnlockRequested?.Invoke(this, EventArgs.Empty);

    /// <inheritdoc />
    public Task<ProvisionedWifiState> RefreshProvisionedWifiAsync(CancellationToken cancellationToken)
    {
        RefreshProvisionedWifiCalls++;
        return Task.FromResult(State.Wifi.Provisioned);
    }

    /// <inheritdoc />
    public Task<ProvisioningOutcome> ApplyProvisioningAsync(CancellationToken cancellationToken)
    {
        ApplyProvisioningCalls++;
        if (ProvisioningGate is not { } gate)
        {
            return Task.FromResult(ProvisioningResult);
        }

        return ProvisioningIgnoresCancellation ? gate.Task : gate.Task.WaitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task ApplyObservationAsync(NetworkObservation observation, CancellationToken cancellationToken) =>
        throw new NotSupportedException("View models never apply observations.");

    /// <inheritdoc />
    public Task<OperationOutcome> ConnectProvisionedWifiAsync(CancellationToken cancellationToken)
    {
        ConnectProvisionedCalls++;
        return OperationGate?.Task ?? Task.FromResult(Next(NetworkOperationKind.ConnectProvisionedWifi));
    }

    /// <inheritdoc />
    public Task<OperationOutcome> ConnectWifiNetworkAsync(WifiSsid ssid, string? passphrase, CancellationToken cancellationToken)
    {
        ConnectNetworkCalls.Add((ssid, passphrase));
        return OperationGate?.Task ?? Task.FromResult(Next(NetworkOperationKind.ConnectWifiNetwork));
    }

    /// <inheritdoc />
    public Task<OperationOutcome> DisconnectWifiAsync(CancellationToken cancellationToken)
    {
        DisconnectCalls++;
        return OperationGate?.Task ?? Task.FromResult(Next(NetworkOperationKind.DisconnectWifi));
    }

    /// <inheritdoc />
    public void ReleaseProvisionedProfile() => ReleaseCalls++;

    private OperationOutcome Next(NetworkOperationKind kind) =>
        Outcomes.Count > 0 ? Outcomes.Dequeue() : OperationOutcome.Success(kind);
}
