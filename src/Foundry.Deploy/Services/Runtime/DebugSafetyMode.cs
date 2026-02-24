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
