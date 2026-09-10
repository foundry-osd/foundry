// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Foundry.Bootstrap.SystemPreparation;

/// <summary>Starts an optional WinPE service and waits for confirmed readiness.</summary>
internal interface IWindowsServiceManager
{
    Task EnsureRunningAsync(string serviceName, CancellationToken cancellationToken);
}

/// <summary>Separates native service mutations and status queries from the bounded waiting policy.</summary>
internal interface IWindowsServiceStatusSource
{
    void Start(string serviceName);

    bool IsRunning(string serviceName);
}

/// <summary>Uses monotonic elapsed time to bound readiness polling independently of clock correction.</summary>
internal sealed class WindowsServiceManager : IWindowsServiceManager
{
    private readonly IWindowsServiceStatusSource _statusSource;
    private readonly TimeSpan _waitTimeout;
    private readonly TimeSpan _pollInterval;

    public WindowsServiceManager()
        : this(new NativeWindowsServiceStatusSource(), TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(200))
    {
    }

    internal WindowsServiceManager(
        IWindowsServiceStatusSource statusSource,
        TimeSpan waitTimeout,
        TimeSpan pollInterval)
    {
        _statusSource = statusSource;
        _waitTimeout = waitTimeout;
        _pollInterval = pollInterval;
    }

    public async Task EnsureRunningAsync(string serviceName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _statusSource.Start(serviceName);
        var wait = Stopwatch.StartNew();
        while (wait.Elapsed <= _waitTimeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_statusSource.IsRunning(serviceName))
            {
                return;
            }

            if (_pollInterval > TimeSpan.Zero)
            {
                await Task.Delay(_pollInterval, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new TimeoutException($"Service '{serviceName}' did not reach the running state within the deadline.");
    }
}

/// <summary>Queries SCM state without parsing localized command output; releases every native handle.</summary>
internal sealed class NativeWindowsServiceStatusSource : IWindowsServiceStatusSource
{
    private const uint ScManagerConnect = 0x0001;
    private const uint ServiceQueryStatus = 0x0004;
    private const uint ServiceStart = 0x0010;
    private const uint ServiceRunning = 0x00000004;
    private const int ErrorServiceAlreadyRunning = 1056;

    public void Start(string serviceName)
    {
        WithService(serviceName, ServiceStart, serviceHandle =>
        {
            if (!StartService(serviceHandle, 0, null))
            {
                int error = Marshal.GetLastWin32Error();
                if (error != ErrorServiceAlreadyRunning)
                {
                    throw new Win32Exception(error);
                }
            }

            return true;
        });
    }

    public bool IsRunning(string serviceName)
    {
        return WithService(serviceName, ServiceQueryStatus, serviceHandle =>
        {
            if (!QueryServiceStatus(serviceHandle, out ServiceStatus status))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            return status.CurrentState == ServiceRunning;
        });
    }

    private static T WithService<T>(string serviceName, uint access, Func<IntPtr, T> action)
    {
        IntPtr managerHandle = OpenSCManager(null, null, ScManagerConnect);
        if (managerHandle == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            IntPtr serviceHandle = OpenService(managerHandle, serviceName, access);
            if (serviceHandle == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            try
            {
                return action(serviceHandle);
            }
            finally
            {
                _ = CloseServiceHandle(serviceHandle);
            }
        }
        finally
        {
            _ = CloseServiceHandle(managerHandle);
        }
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenSCManager(string? machineName, string? databaseName, uint desiredAccess);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenService(IntPtr managerHandle, string serviceName, uint desiredAccess);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool StartService(IntPtr serviceHandle, int argumentCount, string[]? arguments);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceStatus(IntPtr serviceHandle, out ServiceStatus serviceStatus);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
    }
}
