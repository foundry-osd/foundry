// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Provides the WinPE optional component names Foundry selects by default when none are explicitly chosen.
/// </summary>
public static class WinPeOptionalComponentDefaults
{
    /// <summary>
    /// Gets the recommended default WinPE optional component names, matching the components Foundry has
    /// historically added during boot image customization (PowerShell, WMI, scripting, storage, and networking).
    /// </summary>
    public static readonly IReadOnlyList<string> RecommendedComponentNames =
    [
        "WinPE-WMI",
        "WinPE-NetFX",
        "WinPE-Scripting",
        "WinPE-PowerShell",
        "WinPE-WinReCfg",
        "WinPE-DismCmdlets",
        "WinPE-StorageWMI",
        "WinPE-Dot3Svc",
        "WinPE-EnhancedStorage",
        "WinPE-SecureStartup"
    ];

    /// <summary>
    /// Determines whether the supplied component name is a recommended default (case-insensitive).
    /// </summary>
    public static bool IsRecommendedDefault(string componentName)
    {
        return RecommendedComponentNames.Contains(componentName, StringComparer.OrdinalIgnoreCase);
    }
}
