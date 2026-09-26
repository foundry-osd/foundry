// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;

namespace Foundry.Utilities.Imaging;

/// <summary>Shares the process-wide DISM initialization across native image consumers.</summary>
public sealed class DismApiLifetime
{
    private readonly object gate = new();
    private readonly Func<int> initialize;
    private readonly Func<int> shutdown;
    private int owners;

    /// <summary>Gets the lifetime shared by every production DISM adapter in this process.</summary>
    public static DismApiLifetime Shared { get; } = new(() => DismInitialize(0, null, null), DismShutdown);

    internal DismApiLifetime(Func<int> initialize, Func<int> shutdown)
    {
        this.initialize = initialize;
        this.shutdown = shutdown;
    }

    /// <summary>Retains initialization until this consumer has completed all native calls and released its buffers.</summary>
    public IDisposable Acquire()
    {
        lock (gate)
        {
            if (owners == 0) ThrowIfFailed(initialize(), "DismInitialize");
            owners++;
            return new Lease(this);
        }
    }

    private void Release()
    {
        lock (gate)
        {
            if (--owners == 0) ThrowIfFailed(shutdown(), "DismShutdown");
        }
    }

    private static void ThrowIfFailed(int result, string operation)
    {
        if (result != 0) throw new COMException($"{operation} failed with HRESULT 0x{result:X8}.", result);
    }

    private sealed class Lease(DismApiLifetime lifetime) : IDisposable
    {
        private DismApiLifetime? owner = lifetime;
        public void Dispose() => Interlocked.Exchange(ref owner, null)?.Release();
    }

    [DllImport("DismApi.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DismInitialize(uint logLevel, string? logFilePath, string? scratchDirectory);

    [DllImport("DismApi.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DismShutdown();
}
