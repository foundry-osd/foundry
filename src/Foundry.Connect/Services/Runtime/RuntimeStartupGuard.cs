// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using Foundry.Utilities.Runtime;

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
        return RuntimeStartupPolicy.CanRun(WinPeRuntimeDetector.IsWinPeRuntime(), IsDebuggerBypassEnabled());
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
