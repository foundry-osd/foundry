// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Describes a folder that contains drivers to inject into the boot image, and whether it is currently
/// processed (a disabled folder is kept in the configuration but skipped during customization).
/// </summary>
public sealed record WinPeDriverFolder
{
    /// <summary>
    /// Gets the folder that contains drivers (.inf packages).
    /// </summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the folder is injected during customization.
    /// </summary>
    public bool IsEnabled { get; init; } = true;
}
