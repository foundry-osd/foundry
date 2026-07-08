// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Stores the user-authored boot image content choices surfaced by the expert boot media page,
/// such as optional components, PowerShell integration, extra modules, and root folder overlays.
/// </summary>
public sealed record WinPeBootImageContentSettings
{
    /// <summary>
    /// Gets a value indicating whether the minimized troubleshooting PowerShell console is included
    /// in the generated Unattend.xml. Off by default to discourage tampering.
    /// </summary>
    public bool IncludeTroubleshootingConsole { get; init; }

    /// <summary>
    /// Gets a value indicating whether the boot image firewall is enabled. On by default.
    /// </summary>
    public bool EnableFirewall { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether a copy of the boot.wim is kept next to the ISO after creation.
    /// </summary>
    public bool KeepBootWimCopy { get; init; }

    /// <summary>
    /// Gets the WinPE optional component names selected by the user. When empty, Foundry's recommended
    /// defaults are applied during customization.
    /// </summary>
    public IReadOnlyList<string> OptionalComponents { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether PowerShell 7 is integrated into the boot image.
    /// </summary>
    public bool IncludePowerShell7 { get; init; }

    /// <summary>
    /// Gets the selected PowerShell 7 release version (for example <c>7.4.17</c>). When empty, the latest
    /// stable release is resolved at build time.
    /// </summary>
    public string? PowerShell7Version { get; init; }

    /// <summary>
    /// Gets the PowerShell modules selected for integration into the boot image.
    /// </summary>
    public IReadOnlyList<PowerShellModuleSelection> PowerShellModules { get; init; } = [];

    /// <summary>
    /// Gets the additional folders whose contents are copied into the boot image root, preserving the
    /// destination folder structure each folder contains.
    /// </summary>
    public IReadOnlyList<string> AdditionalRootFolderPaths { get; init; } = [];
}
