// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net.NetworkInformation;
using Foundry.Utilities.Networking;
using Foundry.Wpf.Network;

namespace Foundry.Deploy.Services.Network;

/// <summary>
/// Follows the connection state shown next to Deploy's Network menu item by reading the network adapters every
/// <see cref="PollInterval"/>. Deploy runs no internet probe, so it never reports <see cref="NetworkIndicatorState.Limited"/>.
/// </summary>
/// <remarks>
/// <see cref="Start"/> reads the adapters at once on the calling thread; the later reads run on a timer thread and raise
/// <see cref="StateChanged"/> there, only when the state changed. A read that fails keeps the last state. Dispose stops the
/// polling.
/// </remarks>
public sealed class DeployNetworkIndicator : IDisposable
{
    /// <summary>The time between two reads of the adapters.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly INetworkAdapterSnapshotProvider _adapters;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _gate = new();
    private ITimer? _timer;
    private int _isPolling;
    private volatile bool _isDisposed;
    private volatile NetworkIndicatorState _state;

    /// <param name="adapters">Source of the adapter snapshots.</param>
    /// <param name="timeProvider">Clock of the polling timer.</param>
    public DeployNetworkIndicator(INetworkAdapterSnapshotProvider adapters, TimeProvider timeProvider)
    {
        _adapters = adapters ?? throw new ArgumentNullException(nameof(adapters));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <summary>Raised on a timer thread when <see cref="State"/> changed.</summary>
    public event EventHandler? StateChanged;

    /// <summary>Gets the last state read; <see cref="NetworkIndicatorState.Disconnected"/> before <see cref="Start"/>.</summary>
    public NetworkIndicatorState State => _state;

    /// <summary>Reads the state at once, then every <see cref="PollInterval"/>. Later calls do nothing.</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_isDisposed || _timer is not null)
            {
                return;
            }

            if (TryRead(out NetworkIndicatorState state))
            {
                _state = state;
            }

            _timer = _timeProvider.CreateTimer(static state => ((DeployNetworkIndicator)state!).Poll(), this, PollInterval, PollInterval);
        }
    }

    /// <summary>Stops the polling. Idempotent.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _isDisposed = true;
            _timer?.Dispose();
            _timer = null;
        }
    }

    /// <summary>
    /// Maps adapter snapshots to the indicator state: an Ethernet adapter that is up with a routable address wins over a Wi-Fi
    /// adapter in the same state; anything else (loopback, tunnel, down, or only link-local addresses) is not connected.
    /// </summary>
    /// <param name="adapters">The adapters, in platform order.</param>
    /// <returns>The state to show.</returns>
    internal static NetworkIndicatorState Map(IReadOnlyList<NetworkAdapterSnapshot> adapters)
    {
        // UnicastAddresses already excludes loopback, IPv6 link-local and APIPA addresses.
        static bool IsConnected(NetworkAdapterSnapshot adapter) =>
            adapter.OperationalStatus == OperationalStatus.Up && adapter.UnicastAddresses.Count > 0;

        if (adapters.Any(adapter => IsEthernet(adapter.InterfaceType) && IsConnected(adapter)))
        {
            return NetworkIndicatorState.Ethernet;
        }

        return adapters.Any(adapter => adapter.InterfaceType == NetworkInterfaceType.Wireless80211 && IsConnected(adapter))
            ? NetworkIndicatorState.Wifi
            : NetworkIndicatorState.Disconnected;
    }

    private static bool IsEthernet(NetworkInterfaceType type) => type is NetworkInterfaceType.Ethernet
        or NetworkInterfaceType.GigabitEthernet
        or NetworkInterfaceType.FastEthernetFx
        or NetworkInterfaceType.FastEthernetT
        or NetworkInterfaceType.Ethernet3Megabit;

    private void Poll()
    {
        // A slow read must not overlap the next tick.
        if (Interlocked.Exchange(ref _isPolling, 1) == 1)
        {
            return;
        }

        try
        {
            if (_isDisposed || !TryRead(out NetworkIndicatorState state) || state == _state)
            {
                return;
            }

            _state = state;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            Volatile.Write(ref _isPolling, 0);
        }
    }

    private bool TryRead(out NetworkIndicatorState state)
    {
        try
        {
            state = Map(_adapters.GetAdapters());
            return true;
        }
        catch (Exception exception) when (exception is NetworkInformationException or InvalidOperationException or PlatformNotSupportedException)
        {
            state = default;
            return false;
        }
    }
}
