// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;

namespace Foundry.Services.Application;

/// <summary>
/// Observes session ending on the window's UI thread without delaying Windows shutdown or logoff.
/// </summary>
internal sealed class WindowsSessionEndingMonitor : IDisposable
{
    private const uint QueryEndSessionMessage = 0x0011;
    private const uint EndSessionMessage = 0x0016;
    private const uint NonClientDestroyMessage = 0x0082;
    private const nuint SubclassId = 1;
    private readonly nint windowHandle;
    private readonly SubclassCallback callback;
    private readonly int ownerThreadId = Environment.CurrentManagedThreadId;
    private bool disposed;

    /// <summary>
    /// Attaches the rooted native callback on the thread that owns the window.
    /// </summary>
    /// <param name="windowHandle">Native handle of the main WinUI window.</param>
    internal WindowsSessionEndingMonitor(nint windowHandle)
    {
        if (windowHandle == 0 || GetWindowThreadProcessId(windowHandle, out _) != GetCurrentThreadId())
        {
            throw new InvalidOperationException("Session observation must attach to a window on its owning UI thread.");
        }

        this.windowHandle = windowHandle;
        callback = OnWindowMessage;
        if (!SetWindowSubclass(windowHandle, callback, SubclassId, 0))
        {
            throw new InvalidOperationException("Unable to observe Windows session ending for the main window.");
        }
    }

    /// <summary>
    /// Gets whether Windows has queried or confirmed session ending; a canceled end-session resets this value.
    /// </summary>
    internal bool IsSessionEnding { get; private set; }

    /// <summary>
    /// Occurs synchronously in the native callback; subscribers must only enqueue work and return promptly.
    /// </summary>
    internal event EventHandler? StateChanged;

    /// <summary>
    /// Detaches once on the window's UI thread, retaining the delegate until native removal completes.
    /// </summary>
    public void Dispose()
    {
        if (disposed) return;
        if (Environment.CurrentManagedThreadId != ownerThreadId)
        {
            throw new InvalidOperationException("Session observation must detach on its owning UI thread.");
        }

        RemoveWindowSubclass(windowHandle, callback, SubclassId);
        disposed = true;
        GC.KeepAlive(callback);
    }

    private nint OnWindowMessage(nint window, uint message, nuint wParam, nint lParam, nuint subclassId, nuint referenceData)
    {
        if (message == QueryEndSessionMessage)
        {
            SetSessionEnding(true);
            return 1;
        }

        if (message == EndSessionMessage)
        {
            SetSessionEnding(wParam != 0);
            return 0;
        }

        if (message == NonClientDestroyMessage)
        {
            Dispose();
        }

        return DefSubclassProc(window, message, wParam, lParam);
    }

    private void SetSessionEnding(bool value)
    {
        if (IsSessionEnding == value) return;
        IsSessionEnding = value;
        try
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception)
        {
            // Dispatcher teardown must not propagate a managed exception through the native session callback.
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint SubclassCallback(nint window, uint message, nuint wParam, nint lParam, nuint subclassId, nuint referenceData);

    [DllImport("comctl32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(nint window, SubclassCallback callback, nuint subclassId, nuint referenceData);

    [DllImport("comctl32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(nint window, SubclassCallback callback, nuint subclassId);

    [DllImport("comctl32.dll", ExactSpelling = true)]
    private static extern nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern uint GetCurrentThreadId();
}
