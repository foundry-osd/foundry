// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;

namespace Foundry.Core.Services.WinPe;

public sealed record WinPeToolPaths
{
    public string KitsRootPath { get; init; } = string.Empty;
    public string CopypePath { get; init; } = string.Empty;
    public string MakeWinPeMediaPath { get; init; } = string.Empty;
    public string DismPath { get; init; } = string.Empty;
    /// <summary>Gets the native host machine used to select installed DISM.</summary>
    public Architecture HostArchitecture { get; init; }
    /// <summary>Gets the boot image machine, which may differ from the authoring host.</summary>
    public WinPeArchitecture TargetArchitecture { get; init; }
    /// <summary>Gets the selected ADK DISM executable version for source compatibility checks.</summary>
    public Version DismVersion { get; init; } = new(0, 0);
    public string CmdPath { get; init; } = string.Empty;
    public string PowerShellPath { get; init; } = string.Empty;
}
