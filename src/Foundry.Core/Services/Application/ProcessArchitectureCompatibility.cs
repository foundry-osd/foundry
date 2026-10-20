// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;

namespace Foundry.Core.Services.Application;

/// <summary>
/// Decides whether the running Foundry OSD build matches the native architecture of the operating system.
/// </summary>
public static class ProcessArchitectureCompatibility
{
    /// <summary>
    /// Determines whether an x64 build is running under emulation on Windows ARM64 and must be replaced by the ARM64 build.
    /// </summary>
    /// <param name="processArchitecture">The architecture of the running process, such as <see cref="RuntimeInformation.ProcessArchitecture"/>.</param>
    /// <param name="osArchitecture">
    /// The native architecture of the operating system, such as <see cref="RuntimeInformation.OSArchitecture"/>, which
    /// reports the native architecture even when the process runs under emulation.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when an x64 process runs on an ARM64 operating system. Media creation is unreliable in that case
    /// because the Windows ADK environment selects amd64 tools and the amd64 WIM mount driver, which x64 emulation cannot load.
    /// </returns>
    public static bool RequiresArm64Build(Architecture processArchitecture, Architecture osArchitecture)
    {
        return processArchitecture == Architecture.X64 && osArchitecture == Architecture.Arm64;
    }
}
