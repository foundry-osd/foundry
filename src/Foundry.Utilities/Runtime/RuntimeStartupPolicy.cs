// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Utilities.Runtime;

/// <summary>
/// Determines whether an application can start in the current runtime.
/// </summary>
public static class RuntimeStartupPolicy
{
    /// <summary>
    /// Gets whether application startup is permitted.
    /// </summary>
    /// <param name="isWinPeRuntime">Whether the current environment is WinPE.</param>
    /// <param name="debuggerBypassEnabled">Whether the host debugger bypass is enabled.</param>
    /// <returns><see langword="true"/> when startup is allowed.</returns>
    public static bool CanRun(bool isWinPeRuntime, bool debuggerBypassEnabled)
    {
        return isWinPeRuntime || debuggerBypassEnabled;
    }
}
