// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using Foundry.Utilities.Runtime;

namespace Foundry.Connect.Services.Runtime;

/// <summary>Captures the runtime evidence used to decide whether boot-media advice applies.</summary>
internal readonly record struct BootMediaRuntimeContext(string? RuntimeVersion, bool IsWinPeRuntime, bool IsDebuggerAttached, bool IsDebugBuild)
{
    /// <summary>Captures this runtime assembly and process environment without using the authoring application.</summary>
    public static BootMediaRuntimeContext Capture()
    {
#if DEBUG
        const bool isDebugBuild = true;
#else
        const bool isDebugBuild = false;
#endif
        return new BootMediaRuntimeContext(
            typeof(BootMediaRuntimeContext).Assembly.GetName().Version?.ToString(),
            WinPeRuntimeDetector.IsWinPeRuntime(),
            Debugger.IsAttached,
            isDebugBuild);
    }

    /// <summary>Excludes development environments and explicitly debug-provisioned media from update advice.</summary>
    public bool IsEligible(string? provisioningSource)
    {
        return IsWinPeRuntime && !IsDebuggerAttached && !IsDebugBuild &&
            !string.Equals(provisioningSource?.Trim(), "debug", StringComparison.OrdinalIgnoreCase);
    }
}
