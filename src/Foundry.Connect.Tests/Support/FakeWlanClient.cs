// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text;
using Foundry.Connect.Network;
using Foundry.Connect.Network.Native;
using Foundry.Connect.Network.Policy;

namespace Foundry.Connect.Tests.Support;

/// <summary>
/// Scripted <see cref="IWlanClient"/>: returns configured results, records every call and tracks how many connect or
/// disconnect operations overlap.
/// </summary>
internal sealed class FakeWlanClient : IWlanClient
{
    private readonly object _gate = new();
    private int _concurrentOperations;

    public WlanSnapshot Snapshot { get; set; } = new(WifiAvailability.NoAdapter, [], []);

    // Thrown by GetSnapshot when set.
    public Exception? SnapshotException { get; set; }

    public int SnapshotCalls { get; private set; }

    public WlanOperationResult SetProfileResult { get; set; } = new(WlanOperationStatus.Succeeded);

    public Dictionary<(Guid InterfaceId, string ProfileName), string> Profiles { get; } = [];

    // Profile name -> exception thrown by GetProfileXml.
    public Dictionary<string, Exception> ProfileReadFailures { get; } = [];

    public List<(Guid InterfaceId, string Xml)> SetProfileCalls { get; } = [];

    // Empty queue: Succeeded.
    public Queue<WlanOperationResult> ConnectResults { get; } = new();

    public List<(Guid InterfaceId, string ProfileName)> ConnectCalls { get; } = [];

    // When set, ConnectAsync awaits it before returning.
    public TaskCompletionSource? ConnectGate { get; set; }

    public int MaxConcurrentOperations { get; private set; }

    public WlanOperationResult DisconnectResult { get; set; } = new(WlanOperationStatus.Succeeded);

    public List<Guid> DisconnectCalls { get; } = [];

    public static WlanInterfaceSnapshot Interface(Guid id, string? connectedSsid = null, string? profileName = null, WlanAuthAlgorithm algorithm = WlanAuthAlgorithm.RsnaPsk) =>
        new(id, "Wi-Fi", connectedSsid is null ? WlanInterfaceStatus.Disconnected : WlanInterfaceStatus.Connected,
            connectedSsid is null ? null : new WlanCurrentConnection(Encoding.UTF8.GetBytes(connectedSsid), profileName ?? connectedSsid, algorithm, true));

    public static WlanAvailableNetworkInfo Visible(Guid id, string ssid, int signal = 80, WlanAuthAlgorithm algorithm = WlanAuthAlgorithm.RsnaPsk) =>
        new(id, Encoding.UTF8.GetBytes(ssid), algorithm, true, signal);

    public WlanSnapshot GetSnapshot(bool requestScan)
    {
        lock (_gate)
        {
            SnapshotCalls++;
            return SnapshotException is null ? Snapshot : throw SnapshotException;
        }
    }

    public WlanOperationResult SetProfile(Guid interfaceId, string profileXml)
    {
        lock (_gate)
        {
            SetProfileCalls.Add((interfaceId, profileXml));
            if (SetProfileResult.IsSuccess)
            {
                Profiles[(interfaceId, WifiProfileDocument.FromXml(profileXml)!.Name)] = profileXml;
            }

            return SetProfileResult;
        }
    }

    public bool HasProfile(Guid interfaceId, string profileName)
    {
        lock (_gate)
        {
            return Profiles.ContainsKey((interfaceId, profileName));
        }
    }

    public string? GetProfileXml(Guid interfaceId, string profileName)
    {
        lock (_gate)
        {
            return ProfileReadFailures.TryGetValue(profileName, out Exception? failure)
                ? throw failure
                : Profiles.GetValueOrDefault((interfaceId, profileName));
        }
    }

    public async Task<WlanOperationResult> ConnectAsync(Guid interfaceId, string profileName, CancellationToken cancellationToken)
    {
        TaskCompletionSource? gate;
        lock (_gate)
        {
            ConnectCalls.Add((interfaceId, profileName));
            gate = ConnectGate;
        }

        EnterOperation();
        try
        {
            if (gate is not null)
            {
                await gate.Task.WaitAsync(cancellationToken);
            }

            lock (_gate)
            {
                return ConnectResults.TryDequeue(out WlanOperationResult? result) ? result : new WlanOperationResult(WlanOperationStatus.Succeeded);
            }
        }
        finally
        {
            Interlocked.Decrement(ref _concurrentOperations);
        }
    }

    public async Task<WlanOperationResult> DisconnectAsync(Guid interfaceId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            DisconnectCalls.Add(interfaceId);
        }

        EnterOperation();
        try
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                return DisconnectResult;
            }
        }
        finally
        {
            Interlocked.Decrement(ref _concurrentOperations);
        }
    }

    private void EnterOperation()
    {
        int current = Interlocked.Increment(ref _concurrentOperations);
        lock (_gate)
        {
            MaxConcurrentOperations = Math.Max(MaxConcurrentOperations, current);
        }
    }
}
