// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;

namespace Foundry.Deploy.Services.Runtime;

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
