using System.Diagnostics;

namespace Foundry.Connect.Services.Runtime;

internal static class RuntimeStartupGuard
{
    public static bool CanRun()
    {
        return CanRun(WinPeRuntimeDetector.IsWinPeRuntime(), IsDebuggerBypassEnabled());
    }

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
