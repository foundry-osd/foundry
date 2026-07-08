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
    /// Gets the components that must be integrated last, in this exact order. WinPE-SecureBootCmdlets and
    /// WinPE-SecureStartup depend on the rest of the image being present, so they are always applied at the end.
    /// </summary>
    public static readonly IReadOnlyList<string> IntegrationOrderLastComponents =
    [
        "WinPE-SecureBootCmdlets",
        "WinPE-SecureStartup"
    ];

    /// <summary>
    /// Determines whether the supplied component name is a recommended default (case-insensitive).
    /// </summary>
    public static bool IsRecommendedDefault(string componentName)
    {
        return RecommendedComponentNames.Contains(componentName, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Orders the supplied optional components for safe integration: all other components keep their original
    /// relative order, followed by <see cref="IntegrationOrderLastComponents"/> (in that exact order) when present.
    /// </summary>
    public static IReadOnlyList<string> OrderForIntegration(IEnumerable<string> componentNames)
    {
        List<string> components = componentNames.ToList();

        List<string> ordered = components
            .Where(name => !IntegrationOrderLastComponents.Contains(name, StringComparer.OrdinalIgnoreCase))
            .ToList();

        foreach (string last in IntegrationOrderLastComponents)
        {
            string? match = components.FirstOrDefault(name => string.Equals(name, last, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                ordered.Add(match);
            }
        }

        return ordered;
    }
}
