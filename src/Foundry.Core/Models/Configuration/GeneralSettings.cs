// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Stores user-authored media creation settings in the Foundry configuration document.
/// </summary>
public sealed record GeneralSettings
{
    /// <summary>
    /// Gets the optional ISO output path requested by the user.
    /// </summary>
    public string? IsoOutputPath { get; init; }

    /// <summary>
    /// Gets the WinPE architecture used for generated media.
    /// </summary>
    public WinPeArchitecture Architecture { get; init; } = WinPeArchitecture.X64;

    /// <summary>
    /// Gets the optional WinPE language applied to the boot image.
    /// </summary>
    public string? WinPeLanguage { get; init; }

    /// <summary>
    /// Gets a value indicating whether generated media should use the CA 2023 boot signature.
    /// </summary>
    public bool UseCa2023 { get; init; } = true;

    /// <summary>
    /// Gets the partition style used when creating USB media.
    /// </summary>
    public UsbPartitionStyle UsbPartitionStyle { get; init; } = UsbPartitionStyle.Gpt;

    /// <summary>
    /// Gets the formatting mode used when preparing USB media.
    /// </summary>
    public UsbFormatMode UsbFormatMode { get; init; } = UsbFormatMode.Quick;

    /// <summary>
    /// Gets a value indicating whether Dell WinPE drivers are included.
    /// </summary>
    public bool IncludeDellDrivers { get; init; }

    /// <summary>
    /// Gets a value indicating whether HP WinPE drivers are included.
    /// </summary>
    public bool IncludeHpDrivers { get; init; }

    /// <summary>
    /// Gets an optional directory containing additional WinPE drivers.
    /// </summary>
    public string? CustomDriverDirectoryPath { get; init; }

    /// <summary>
    /// Gets the expert boot image content choices (optional components, PowerShell integration,
    /// extra modules, root folder overlays, and boot behavior toggles).
    /// </summary>
    public WinPeBootImageContentSettings BootImageContent { get; init; } = new();
}
