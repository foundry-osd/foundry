using System.Diagnostics;

namespace Foundry.Connect.Services.Runtime;

/// <summary>
/// Prevents Foundry.Connect from running outside WinPE except during DEBUG debugger sessions.
/// </summary>
internal static class RuntimeStartupGuard
{
    /// <summary>
    /// Gets whether the current process is allowed to continue startup.
    /// </summary>
    /// <returns><see langword="true"/> when the runtime is WinPE or a debugger bypass is active.</returns>
    public static bool CanRun()
    {
        return CanRun(WinPeRuntimeDetector.IsWinPeRuntime(), IsDebuggerBypassEnabled());
    }

    /// <summary>
    /// Evaluates startup permission from supplied runtime facts.
    /// </summary>
    /// <param name="isWinPeRuntime">Whether the current environment is WinPE.</param>
    /// <param name="isDebuggerBypassEnabled">Whether the debug bypass is enabled.</param>
    /// <returns><see langword="true"/> when startup is allowed.</returns>
    internal static bool CanRun(bool isWinPeRuntime, bool isDebuggerBypassEnabled)
    {
        return isWinPeRuntime || isDebuggerBypassEnabled;
    }

    private static bool IsDebuggerBypassEnabled()
    {
#if DEBUG
        return Debugger.IsAttached;
#else
        return false;
#endif
    }
}
