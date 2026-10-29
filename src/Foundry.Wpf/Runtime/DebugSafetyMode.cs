// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;

namespace Foundry.Wpf.Runtime;

/// <summary>
/// Gates debug-only scenarios and shortcuts that simulate failures or skip safeguards. It is enabled only in
/// a Debug build with a debugger attached, so such code paths can never be reached in a shipped runtime.
/// </summary>
public static class DebugSafetyMode
{
    /// <summary>Gets whether debug-only behavior may run; always <see langword="false"/> in Release builds.</summary>
    public static bool IsEnabled
    {
        get
        {
#if DEBUG
            return Debugger.IsAttached;
#else
            return false;
#endif
        }
    }
}
