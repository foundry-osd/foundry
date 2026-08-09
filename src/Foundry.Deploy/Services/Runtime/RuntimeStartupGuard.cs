// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using Foundry.Utilities.Runtime;

namespace Foundry.Deploy.Services.Runtime;

internal static class RuntimeStartupGuard
{
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
