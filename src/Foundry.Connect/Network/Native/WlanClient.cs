// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Foundry.Connect.Network.Native;

/// <summary>
/// Native Wifi implementation of <see cref="IWlanClient"/>.
/// </summary>
/// <remarks>
/// Every operation opens and closes its own client handle. WLAN failures are returned as results and logged at
/// Debug only; the connection coordinator owns Information and Warning logging. SSIDs and profile XML are never logged.
/// </remarks>
public sealed class WlanClient : IWlanClient
{
    /// <summary>
    /// Gets the time allowed for a connection attempt to report its terminal notification.
    /// </summary>
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Gets the time allowed for an interface to leave the connected state after a disconnect request.
    /// </summary>
    public static readonly TimeSpan DisconnectTimeout = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan DisconnectPollInterval = TimeSpan.FromMilliseconds(500);
    private const int ProfileConnectionMode = 0; // wlan_connection_mode_profile
    private const int InfrastructureBssType = 1; // dot11_BSS_type_infrastructure
    private const int ReasonTextLength = 256;

    // wlanapi keeps a raw function pointer while a registration exists; a static delegate is never collected.
    private static readonly NativeWifiApi.WlanNotificationCallback NotificationCallback = OnNotification;
    private static readonly ConcurrentDictionary<long, PendingConnect> PendingConnects = new();
    private static long _lastConnectId;

    private readonly TimeProvider _timeProvider;
    private readonly ILogger<WlanClient> _logger;
    private readonly Lock _snapshotFailureLock = new();
    private string? _lastSnapshotFailure;

    /// <summary>
    /// Initializes a new instance of the <see cref="WlanClient"/> class.
    /// </summary>
    /// <param name="timeProvider">Clock for the connect timeout and the disconnect polling.</param>
    /// <param name="logger">Debug logger for WLAN failures.</param>
    public WlanClient(TimeProvider timeProvider, ILogger<WlanClient> logger)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public WlanSnapshot GetSnapshot(bool requestScan)
    {
        // Snapshots are polled; only the first failure of a read is kept and logged when it changes.
        string? failure = null;
        void RecordFailure(string nativeCall, uint error) => failure ??= $"{nativeCall}:{error}";

        try
        {
            WlanSnapshot snapshot = ReadSnapshot(requestScan, RecordFailure);
            ReportSnapshotFailure(failure);
            return snapshot;
        }
        catch (Exception exception)
        {
            // The exception is not logged: its message can carry identifying data and every level is mirrored remotely.
            ReportSnapshotFailure(FormattableString.Invariant($"{exception.GetType().Name}:{NetworkFailureTaxonomy.ExceptionErrorCode(exception)}"));
            return WlanSnapshot.RuntimeMissing(0);
        }
    }

    /// <inheritdoc />
    public WlanOperationResult SetProfile(Guid interfaceId, string profileXml)
    {
        ArgumentException.ThrowIfNullOrEmpty(profileXml);

        try
        {
            using WlanClientHandle? handle = OpenHandle(out uint openError, LogCallFailure);
            if (handle is null)
            {
                return MapOpenFailure(openError);
            }

            uint error = NativeWifiApi.WlanSetProfile(
                handle,
                in interfaceId,
                0,
                profileXml,
                null,
                true,
                IntPtr.Zero,
                out uint reasonCode);
            WlanOperationResult result = MapSetProfileResult(error, reasonCode);
            if (!result.IsSuccess)
            {
                LogWlanFailure(nameof(NativeWifiApi.WlanSetProfile), error, reasonCode);
            }

            return result;
        }
        catch (Exception exception) when (IsRuntimeMissing(exception))
        {
            LogRuntimeMissing(exception, nameof(NativeWifiApi.WlanSetProfile));
            return MapOpenFailure(0);
        }
    }

    /// <inheritdoc />
    public bool HasProfile(Guid interfaceId, string profileName)
    {
        return TryGetProfile(interfaceId, profileName, flags: 0, out _);
    }

    /// <inheritdoc />
    public string? GetProfileXml(Guid interfaceId, string profileName)
    {
        return TryGetProfile(interfaceId, profileName, NativeWifiApi.ProfileGetPlaintextKey, out string? profileXml)
            ? profileXml
            : null;
    }

    /// <inheritdoc />
    public async Task<WlanOperationResult> ConnectAsync(Guid interfaceId, string profileName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(profileName);
        cancellationToken.ThrowIfCancellationRequested();

        long connectId = Interlocked.Increment(ref _lastConnectId);
        PendingConnect pending = new(interfaceId, profileName);
        PendingConnects[connectId] = pending;
        WlanClientHandle? handle = null;
        IntPtr profilePointer = IntPtr.Zero;

        try
        {
            handle = OpenHandle(out uint openError, LogCallFailure);
            if (handle is null)
            {
                return MapOpenFailure(openError);
            }

            uint error = NativeWifiApi.WlanRegisterNotification(
                handle,
                NativeWifiApi.NotificationSourceAcm,
                true,
                NotificationCallback,
                new IntPtr(connectId),
                IntPtr.Zero,
                IntPtr.Zero);
            if (error != 0)
            {
                LogWlanFailure(nameof(NativeWifiApi.WlanRegisterNotification), error, 0);
                return new WlanOperationResult(WlanOperationStatus.RequestFailed, error);
            }

            profilePointer = Marshal.StringToHGlobalUni(profileName);
            NativeWifiApi.WlanConnectionParameters parameters = new()
            {
                ConnectionMode = ProfileConnectionMode,
                Profile = profilePointer,
                BssType = InfrastructureBssType
            };

            error = NativeWifiApi.WlanConnect(handle, in interfaceId, in parameters, IntPtr.Zero);
            if (error != 0)
            {
                LogWlanFailure(nameof(NativeWifiApi.WlanConnect), error, 0);
                return new WlanOperationResult(WlanOperationStatus.RequestFailed, error);
            }

            WlanOperationResult result;
            try
            {
                result = await pending.Completion.Task
                    .WaitAsync(ConnectTimeout, _timeProvider, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // A lost or late notification must not hide a connection that did succeed.
                result = ReadConnectionAfterTimeout(handle, interfaceId, profileName, pending.LastReason);
            }

            if (!result.IsSuccess)
            {
                LogWlanFailure(nameof(NativeWifiApi.WlanConnect), result.Win32Error, result.ReasonCode);
            }

            return result;
        }
        catch (Exception exception) when (IsRuntimeMissing(exception))
        {
            LogRuntimeMissing(exception, nameof(NativeWifiApi.WlanConnect));
            return MapOpenFailure(0);
        }
        finally
        {
            // Closing the handle ends the registration and waits for a running callback before the entry goes away.
            handle?.Dispose();
            if (profilePointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(profilePointer);
            }

            PendingConnects.TryRemove(connectId, out _);
        }
    }

    /// <inheritdoc />
    public async Task<WlanOperationResult> DisconnectAsync(Guid interfaceId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using WlanClientHandle? handle = OpenHandle(out uint openError, LogCallFailure);
            if (handle is null)
            {
                return MapOpenFailure(openError);
            }

            uint error = NativeWifiApi.WlanDisconnect(handle, in interfaceId, IntPtr.Zero);
            if (error != 0)
            {
                LogWlanFailure(nameof(NativeWifiApi.WlanDisconnect), error, 0);
                return new WlanOperationResult(WlanOperationStatus.RequestFailed, error);
            }

            long startedAt = _timeProvider.GetTimestamp();
            while (true)
            {
                List<WlanInterfaceSnapshot>? interfaces = ReadInterfaces(handle, out uint enumError, LogCallFailure);
                if (interfaces is null)
                {
                    return new WlanOperationResult(WlanOperationStatus.RequestFailed, enumError);
                }

                // An interface that disappeared is no longer connected either.
                WlanInterfaceSnapshot? wlanInterface = interfaces.Find(candidate => candidate.InterfaceId == interfaceId);
                if (wlanInterface?.Status != WlanInterfaceStatus.Connected)
                {
                    return new WlanOperationResult(WlanOperationStatus.Succeeded);
                }

                if (_timeProvider.GetElapsedTime(startedAt) >= DisconnectTimeout)
                {
                    _logger.LogDebug("WLAN interface is still connected after the disconnect timeout. NativeCall={NativeCall}", nameof(NativeWifiApi.WlanDisconnect));
                    return new WlanOperationResult(WlanOperationStatus.TimedOut);
                }

                await Task.Delay(DisconnectPollInterval, _timeProvider, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (IsRuntimeMissing(exception))
        {
            LogRuntimeMissing(exception, nameof(NativeWifiApi.WlanDisconnect));
            return MapOpenFailure(0);
        }
    }

    /// <summary>
    /// Groups the native interface state: every transitional association state is <see cref="WlanInterfaceStatus.Connecting"/>.
    /// </summary>
    /// <param name="state">Native interface state.</param>
    /// <returns>The grouped status.</returns>
    internal static WlanInterfaceStatus MapInterfaceState(NativeWifiApi.WlanInterfaceState state)
    {
        return state switch
        {
            NativeWifiApi.WlanInterfaceState.Connected or NativeWifiApi.WlanInterfaceState.AdHocNetworkFormed => WlanInterfaceStatus.Connected,
            NativeWifiApi.WlanInterfaceState.Associating
                or NativeWifiApi.WlanInterfaceState.Discovering
                or NativeWifiApi.WlanInterfaceState.Authenticating => WlanInterfaceStatus.Connecting,
            NativeWifiApi.WlanInterfaceState.Disconnecting => WlanInterfaceStatus.Disconnecting,
            NativeWifiApi.WlanInterfaceState.Disconnected => WlanInterfaceStatus.Disconnected,
            _ => WlanInterfaceStatus.NotReady
        };
    }

    /// <summary>
    /// Maps a <c>WlanSetProfile</c> result; both codes are always carried for diagnostics.
    /// </summary>
    /// <param name="error">Win32 error returned by <c>WlanSetProfile</c>.</param>
    /// <param name="reasonCode">WLAN reason code filled by <c>WlanSetProfile</c>.</param>
    /// <returns><see cref="WlanOperationStatus.ProfileRejected"/> for <c>ERROR_BAD_PROFILE</c>, otherwise success or a request failure.</returns>
    internal static WlanOperationResult MapSetProfileResult(uint error, uint reasonCode)
    {
        WlanOperationStatus status = error switch
        {
            0 => WlanOperationStatus.Succeeded,
            NativeWifiApi.ErrorBadProfile => WlanOperationStatus.ProfileRejected,
            _ => WlanOperationStatus.RequestFailed
        };

        return new WlanOperationResult(status, error, reasonCode);
    }

    /// <summary>
    /// Maps an ACM connection notification for the pending profile.
    /// </summary>
    /// <remarks>
    /// Only <c>wlan_notification_acm_connection_complete</c> is terminal: wlanapi may still try other networks after
    /// <c>connection_attempt_fail</c>, whose reason is kept for a timeout.
    /// </remarks>
    /// <param name="notificationCode">ACM notification code.</param>
    /// <param name="reasonCode">WLAN reason code of the notification.</param>
    /// <returns>The terminal result, or <see langword="null"/> when the notification is not terminal.</returns>
    internal static WlanOperationResult? MapConnectionNotification(uint notificationCode, uint reasonCode)
    {
        if (notificationCode != NativeWifiApi.AcmConnectionComplete)
        {
            return null;
        }

        return reasonCode == 0
            ? new WlanOperationResult(WlanOperationStatus.Succeeded)
            : new WlanOperationResult(WlanOperationStatus.ConnectionFailed, ReasonCode: reasonCode);
    }

    /// <summary>
    /// Maps a failed <c>WlanOpenHandle</c>: any nonzero result (for example a stopped WLAN service) means the runtime
    /// cannot be used.
    /// </summary>
    /// <param name="error">Win32 error, or 0 when the library or an entry point is missing.</param>
    /// <returns>A <see cref="WlanOperationStatus.RuntimeMissing"/> result.</returns>
    internal static WlanOperationResult MapOpenFailure(uint error)
    {
        return new WlanOperationResult(WlanOperationStatus.RuntimeMissing, error);
    }

    private static void OnNotification(IntPtr data, IntPtr context)
    {
        // Runs on a wlanapi thread: no WLAN call here, and no exception may escape into native code.
        try
        {
            if (data == IntPtr.Zero || !PendingConnects.TryGetValue(context.ToInt64(), out PendingConnect? pending))
            {
                return;
            }

            NativeWifiApi.WlanNotificationData notification = Marshal.PtrToStructure<NativeWifiApi.WlanNotificationData>(data);
            if (notification.NotificationSource != NativeWifiApi.NotificationSourceAcm ||
                notification.InterfaceGuid != pending.InterfaceId ||
                notification.NotificationCode is not (NativeWifiApi.AcmConnectionComplete or NativeWifiApi.AcmConnectionAttemptFail) ||
                notification.Data == IntPtr.Zero ||
                notification.DataSize < Marshal.SizeOf<NativeWifiApi.WlanConnectionNotificationHeader>())
            {
                return;
            }

            NativeWifiApi.WlanConnectionNotificationHeader header =
                Marshal.PtrToStructure<NativeWifiApi.WlanConnectionNotificationHeader>(notification.Data);
            if (!string.Equals(header.ProfileName, pending.ProfileName, StringComparison.Ordinal))
            {
                return;
            }

            if (notification.NotificationCode == NativeWifiApi.AcmConnectionAttemptFail)
            {
                pending.LastReason = header.ReasonCode;
            }

            WlanOperationResult? result = MapConnectionNotification(notification.NotificationCode, header.ReasonCode);
            if (result is not null)
            {
                pending.Completion.TrySetResult(result);
            }
        }
        catch (Exception)
        {
            // A malformed notification is ignored; the connect timeout then reads the interface state.
        }
    }

    private static bool IsRuntimeMissing(Exception exception)
    {
        return exception is DllNotFoundException or EntryPointNotFoundException;
    }

    private static byte[] ReadSsidBytes(NativeWifiApi.Dot11Ssid ssid)
    {
        if (ssid.Value is null)
        {
            return [];
        }

        int length = (int)Math.Min(ssid.Length, (uint)ssid.Value.Length);
        return ssid.Value.AsSpan(0, length).ToArray();
    }

    private static List<NativeWifiApi.WlanInterfaceInfo> ReadInterfaceInfos(IntPtr interfaceListPointer)
    {
        if (interfaceListPointer == IntPtr.Zero)
        {
            return [];
        }

        NativeWifiApi.WlanInterfaceInfoListHeader header =
            Marshal.PtrToStructure<NativeWifiApi.WlanInterfaceInfoListHeader>(interfaceListPointer);
        int itemOffset = Marshal.SizeOf<NativeWifiApi.WlanInterfaceInfoListHeader>();
        int itemSize = Marshal.SizeOf<NativeWifiApi.WlanInterfaceInfo>();
        List<NativeWifiApi.WlanInterfaceInfo> items = new(header.NumberOfItems);

        for (int index = 0; index < header.NumberOfItems; index++)
        {
            IntPtr itemPointer = IntPtr.Add(interfaceListPointer, itemOffset + (index * itemSize));
            items.Add(Marshal.PtrToStructure<NativeWifiApi.WlanInterfaceInfo>(itemPointer));
        }

        return items;
    }

    /// <summary>
    /// Logs a snapshot failure only when it differs from the previous snapshot's, and once when snapshots recover.
    /// </summary>
    /// <remarks>
    /// The monitor polls snapshots; a missing runtime would otherwise log the same failure on every refresh.
    /// </remarks>
    /// <param name="failure">Failing native call and error (or exception type and code), or <see langword="null"/> for a clean read.</param>
    internal void ReportSnapshotFailure(string? failure)
    {
        string? previous;
        lock (_snapshotFailureLock)
        {
            previous = _lastSnapshotFailure;
            _lastSnapshotFailure = failure;
        }

        if (string.Equals(previous, failure, StringComparison.Ordinal))
        {
            return;
        }

        if (failure is null)
        {
            _logger.LogDebug("WLAN snapshot read succeeded again. PreviousSnapshotFailure={SnapshotFailure}", previous);
            return;
        }

        _logger.LogDebug("WLAN snapshot read failed. SnapshotFailure={SnapshotFailure}", failure);
    }

    private WlanSnapshot ReadSnapshot(bool requestScan, Action<string, uint> reportFailure)
    {
        using WlanClientHandle? handle = OpenHandle(out uint openError, reportFailure);
        if (handle is null)
        {
            return WlanSnapshot.RuntimeMissing(openError);
        }

        List<WlanInterfaceSnapshot>? interfaces = ReadInterfaces(handle, out uint enumError, reportFailure);
        if (interfaces is null)
        {
            return WlanSnapshot.RuntimeMissing(enumError);
        }

        if (interfaces.Count == 0)
        {
            return new WlanSnapshot(WifiAvailability.NoAdapter, [], []);
        }

        List<WlanAvailableNetworkInfo> networks = [];
        foreach (WlanInterfaceSnapshot wlanInterface in interfaces)
        {
            networks.AddRange(ReadAvailableNetworks(handle, wlanInterface.InterfaceId, reportFailure));
            if (requestScan)
            {
                _ = NativeWifiApi.WlanScan(handle, wlanInterface.InterfaceId, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            }
        }

        return new WlanSnapshot(WifiAvailability.Available, interfaces, networks);
    }

    private static WlanClientHandle? OpenHandle(out uint error, Action<string, uint> reportFailure)
    {
        error = NativeWifiApi.WlanOpenHandle(NativeWifiApi.ClientVersion, IntPtr.Zero, out _, out WlanClientHandle handle);
        if (error == 0)
        {
            return handle;
        }

        handle.Dispose();
        reportFailure(nameof(NativeWifiApi.WlanOpenHandle), error);
        return null;
    }

    private static List<WlanInterfaceSnapshot>? ReadInterfaces(
        WlanClientHandle handle,
        out uint error,
        Action<string, uint> reportFailure)
    {
        IntPtr interfaceListPointer = IntPtr.Zero;

        try
        {
            error = NativeWifiApi.WlanEnumInterfaces(handle, IntPtr.Zero, out interfaceListPointer);
            if (error != 0)
            {
                reportFailure(nameof(NativeWifiApi.WlanEnumInterfaces), error);
                return null;
            }

            List<WlanInterfaceSnapshot> interfaces = [];
            foreach (NativeWifiApi.WlanInterfaceInfo info in ReadInterfaceInfos(interfaceListPointer))
            {
                WlanInterfaceStatus status = MapInterfaceState(info.State);
                WlanCurrentConnection? connection = status == WlanInterfaceStatus.Connected
                    ? ReadCurrentConnection(handle, info.InterfaceGuid, reportFailure)
                    : null;
                interfaces.Add(new WlanInterfaceSnapshot(info.InterfaceGuid, info.InterfaceDescription ?? string.Empty, status, connection));
            }

            return interfaces;
        }
        finally
        {
            NativeWifiApi.FreeMemory(interfaceListPointer);
        }
    }

    private static WlanCurrentConnection? ReadCurrentConnection(
        WlanClientHandle handle,
        Guid interfaceId,
        Action<string, uint> reportFailure)
    {
        IntPtr dataPointer = IntPtr.Zero;

        try
        {
            uint error = NativeWifiApi.WlanQueryInterface(
                handle,
                in interfaceId,
                NativeWifiApi.WlanIntfOpcode.CurrentConnection,
                IntPtr.Zero,
                out _,
                out dataPointer,
                out _);
            if (error != 0)
            {
                reportFailure(nameof(NativeWifiApi.WlanQueryInterface), error);
                return null;
            }

            if (dataPointer == IntPtr.Zero)
            {
                return null;
            }

            NativeWifiApi.WlanConnectionAttributes attributes =
                Marshal.PtrToStructure<NativeWifiApi.WlanConnectionAttributes>(dataPointer);
            return new WlanCurrentConnection(
                ReadSsidBytes(attributes.AssociationAttributes.Ssid),
                attributes.ProfileName ?? string.Empty,
                (WlanAuthAlgorithm)attributes.SecurityAttributes.AuthAlgorithm,
                attributes.SecurityAttributes.SecurityEnabled);
        }
        finally
        {
            NativeWifiApi.FreeMemory(dataPointer);
        }
    }

    private static List<WlanAvailableNetworkInfo> ReadAvailableNetworks(
        WlanClientHandle handle,
        Guid interfaceId,
        Action<string, uint> reportFailure)
    {
        IntPtr networkListPointer = IntPtr.Zero;

        try
        {
            uint error = NativeWifiApi.WlanGetAvailableNetworkList(
                handle,
                in interfaceId,
                NativeWifiApi.AvailableNetworkIncludeAllAdhocProfiles,
                IntPtr.Zero,
                out networkListPointer);
            if (error != 0)
            {
                reportFailure(nameof(NativeWifiApi.WlanGetAvailableNetworkList), error);
                return [];
            }

            if (networkListPointer == IntPtr.Zero)
            {
                return [];
            }

            NativeWifiApi.WlanAvailableNetworkListHeader header =
                Marshal.PtrToStructure<NativeWifiApi.WlanAvailableNetworkListHeader>(networkListPointer);
            int itemOffset = Marshal.SizeOf<NativeWifiApi.WlanAvailableNetworkListHeader>();
            int itemSize = Marshal.SizeOf<NativeWifiApi.WlanAvailableNetwork>();
            List<WlanAvailableNetworkInfo> networks = new(header.NumberOfItems);

            for (int index = 0; index < header.NumberOfItems; index++)
            {
                IntPtr itemPointer = IntPtr.Add(networkListPointer, itemOffset + (index * itemSize));
                NativeWifiApi.WlanAvailableNetwork item = Marshal.PtrToStructure<NativeWifiApi.WlanAvailableNetwork>(itemPointer);
                networks.Add(new WlanAvailableNetworkInfo(
                    interfaceId,
                    ReadSsidBytes(item.Ssid),
                    (WlanAuthAlgorithm)item.DefaultAuthAlgorithm,
                    item.SecurityEnabled,
                    (int)Math.Clamp(item.SignalQuality, 0u, 100u)));
            }

            return networks;
        }
        finally
        {
            NativeWifiApi.FreeMemory(networkListPointer);
        }
    }

    private bool TryGetProfile(Guid interfaceId, string profileName, uint flags, out string? profileXml)
    {
        ArgumentException.ThrowIfNullOrEmpty(profileName);
        profileXml = null;
        IntPtr profileXmlPointer = IntPtr.Zero;

        try
        {
            using WlanClientHandle? handle = OpenHandle(out _, LogCallFailure);
            if (handle is null)
            {
                return false;
            }

            uint error = NativeWifiApi.WlanGetProfile(
                handle,
                in interfaceId,
                profileName,
                IntPtr.Zero,
                out profileXmlPointer,
                ref flags,
                out _);
            if (error != 0)
            {
                // ERROR_NOT_FOUND is the normal answer for a profile that is not there yet.
                if (error != NativeWifiApi.ErrorNotFound)
                {
                    LogWlanFailure(nameof(NativeWifiApi.WlanGetProfile), error, 0);
                }

                return false;
            }

            profileXml = profileXmlPointer == IntPtr.Zero ? null : Marshal.PtrToStringUni(profileXmlPointer);
            return profileXml is not null;
        }
        catch (Exception exception) when (IsRuntimeMissing(exception))
        {
            LogRuntimeMissing(exception, nameof(NativeWifiApi.WlanGetProfile));
            return false;
        }
        finally
        {
            // The buffer may hold the plaintext key; clear it before it goes back to the wlanapi heap.
            ClearUnicodeString(profileXmlPointer);
            NativeWifiApi.FreeMemory(profileXmlPointer);
        }
    }

    private static void ClearUnicodeString(IntPtr pointer)
    {
        if (pointer == IntPtr.Zero)
        {
            return;
        }

        int length = 0;
        while (Marshal.ReadInt16(pointer, length * sizeof(char)) != 0)
        {
            length++;
        }

        int byteCount = (length + 1) * sizeof(char);
        Marshal.Copy(new byte[byteCount], 0, pointer, byteCount);
    }

    private WlanOperationResult ReadConnectionAfterTimeout(
        WlanClientHandle handle,
        Guid interfaceId,
        string profileName,
        uint lastReason)
    {
        List<WlanInterfaceSnapshot>? interfaces = ReadInterfaces(handle, out _, LogCallFailure);
        WlanInterfaceSnapshot? wlanInterface = interfaces?.Find(candidate => candidate.InterfaceId == interfaceId);
        if (interfaces is not null && wlanInterface is null)
        {
            // The adapter went away during the attempt (for example an unplugged USB adapter).
            return new WlanOperationResult(WlanOperationStatus.InterfaceNotFound, ReasonCode: lastReason);
        }

        bool connectedWithProfile = wlanInterface?.Status == WlanInterfaceStatus.Connected &&
            string.Equals(wlanInterface.Connection?.ProfileName, profileName, StringComparison.Ordinal);

        return connectedWithProfile
            ? new WlanOperationResult(WlanOperationStatus.Succeeded)
            : new WlanOperationResult(WlanOperationStatus.TimedOut, ReasonCode: lastReason);
    }

    private void LogWlanFailure(string nativeCall, uint win32Error, uint reasonCode)
    {
        _logger.LogDebug(
            "WLAN call failed. NativeCall={NativeCall} Win32Error={Win32Error} ReasonCode={ReasonCode} ReasonText={ReasonText}",
            nativeCall,
            win32Error,
            reasonCode,
            ReadReasonText(reasonCode));
    }

    private void LogCallFailure(string nativeCall, uint win32Error)
    {
        LogWlanFailure(nativeCall, win32Error, 0);
    }

    // The exception is not attached (every level is mirrored remotely); its type and HRESULT identify the cause.
    private void LogRuntimeMissing(Exception exception, string nativeCall)
    {
        _logger.LogDebug(
            "WLAN runtime is not available. NativeCall={NativeCall}, FailureType={FailureType}, ErrorCode={ErrorCode}",
            nativeCall,
            exception.GetType().Name,
            NetworkFailureTaxonomy.ExceptionErrorCode(exception));
    }

    private static string? ReadReasonText(uint reasonCode)
    {
        if (reasonCode == 0)
        {
            return null;
        }

        try
        {
            char[] buffer = new char[ReasonTextLength];
            uint error = NativeWifiApi.WlanReasonCodeToString(reasonCode, (uint)buffer.Length, buffer, IntPtr.Zero);
            if (error != 0)
            {
                return null;
            }

            int length = Array.IndexOf(buffer, '\0');
            return new string(buffer, 0, length < 0 ? buffer.Length : length);
        }
        catch (Exception exception) when (IsRuntimeMissing(exception))
        {
            return null;
        }
    }

    /// <summary>
    /// Connection attempt waiting for its terminal ACM notification.
    /// </summary>
    private sealed class PendingConnect(Guid interfaceId, string profileName)
    {
        private uint _lastReason;

        public Guid InterfaceId { get; } = interfaceId;

        public string ProfileName { get; } = profileName;

        // Completed from a wlanapi thread; continuations must not run there.
        public TaskCompletionSource<WlanOperationResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        // Written by the callback thread and read by the waiting operation.
        public uint LastReason
        {
            get => Volatile.Read(ref _lastReason);
            set => Volatile.Write(ref _lastReason, value);
        }
    }
}
