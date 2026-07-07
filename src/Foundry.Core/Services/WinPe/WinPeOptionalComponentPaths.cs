// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Resolves ADK WinPE optional component (WinPE_OCs) paths.
/// </summary>
internal static class WinPeOptionalComponentPaths
{
    /// <summary>
    /// Gets the WinPE_OCs root folder for the supplied ADK kits root and architecture.
    /// </summary>
    public static string GetOptionalComponentsRootPath(string kitsRootPath, WinPeArchitecture architecture)
    {
        return Path.Combine(
            kitsRootPath,
            "Assessment and Deployment Kit",
            "Windows Preinstallation Environment",
            architecture.ToCopypeArchitecture(),
            "WinPE_OCs");
    }
}
