// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Describes a PXE boot folder to write from a prepared WinPE workspace.
/// </summary>
public sealed record WinPePxeMediaOptions
{
    /// <summary>Gets the workspace whose boot image and boot files are copied.</summary>
    public WinPeWorkspacePreparationResult? PreparedWorkspace { get; init; }

    /// <summary>Gets the folder that receives the boot files; it is created when missing.</summary>
    public string OutputDirectoryPath { get; init; } = string.Empty;

    /// <summary>Gets the receiver of copy progress.</summary>
    public IProgress<WinPeMediaProgress>? Progress { get; init; }
}
