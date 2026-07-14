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
    /// Gets the shortcut that opens an interactive PowerShell troubleshooting console from Foundry.Connect
    /// and Foundry.Deploy. Off by default to discourage tampering.
    /// </summary>
    public TroubleshootingConsoleSettings TroubleshootingConsole { get; init; } = new();

    /// <summary>
    /// Gets a value indicating whether the boot image firewall is enabled. On by default.
    /// </summary>
    public bool EnableFirewall { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether a copy of the boot.wim is kept next to the ISO after creation.
    /// </summary>
    public bool KeepBootWimCopy { get; init; }

    /// <summary>
    /// Gets the JPEG image used as the WinPE desktop background. It is copied into the boot image as
    /// <c>%WINDIR%\System32\winpe.jpg</c>. When empty, the stock background is kept.
    /// </summary>
    public string? WallpaperPath { get; init; }

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
    /// Gets the additional folders whose contents are copied into a relative destination inside the boot
    /// image (the destination is relative to the image root).
    /// </summary>
    public IReadOnlyList<WinPeAdditionalRootFolder> AdditionalRootFolders { get; init; } = [];

    /// <summary>
    /// Gets the folders that contain drivers (.inf packages) to inject into the boot image. Each folder can be
    /// individually enabled or disabled without removing it.
    /// </summary>
    public IReadOnlyList<WinPeDriverFolder> DriverFolders { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether a driver package that fails to inject is skipped (with a warning)
    /// so the build continues, instead of failing media creation. On by default.
    /// </summary>
    public bool ContinueOnDriverError { get; init; } = true;
}
