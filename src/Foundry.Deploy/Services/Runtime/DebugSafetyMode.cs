// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;

namespace Foundry.Deploy.Services.Runtime;

public static class DebugSafetyMode
{
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
