// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Services.Adk;

/// <summary>
/// Describes the detected Windows ADK and WinPE add-on state.
/// </summary>
/// <param name="IsInstalled">Whether a Windows ADK installation was detected.</param>
/// <param name="IsCompatible">Whether the detected ADK version satisfies Foundry requirements.</param>
/// <param name="IsWinPeAddonInstalled">Whether the WinPE add-on is installed.</param>
/// <param name="InstalledVersion">The detected ADK version, when available.</param>
/// <param name="VersionRelation">How the detected ADK version compares to the supported build line.</param>
/// <param name="KitsRootPath">The detected Windows Kits root path, when available.</param>
/// <param name="RequiredVersionPolicy">The version policy used to evaluate compatibility.</param>
/// <param name="ServicingState">Whether required ADK component patch registrations are verified.</param>
/// <param name="IsWinPeAddonCompatible">Whether the installed WinPE components match the ADK release.</param>
/// <param name="IsX64Available">Whether required x64 image, optional component and boot files exist.</param>
/// <param name="IsArm64Available">Whether required ARM64 image, optional component and boot files exist.</param>
/// <param name="IsWinPeAddonRegistered">Whether Windows Installer records any WinPE component, even if its files are missing.</param>
public sealed record AdkInstallationStatus(
    bool IsInstalled,
    bool IsCompatible,
    bool IsWinPeAddonInstalled,
    string? InstalledVersion,
    AdkVersionRelation VersionRelation,
    string? KitsRootPath,
    string RequiredVersionPolicy,
    AdkServicingState ServicingState = AdkServicingState.Unknown,
    bool IsWinPeAddonCompatible = false,
    bool IsX64Available = false,
    bool IsArm64Available = false,
    bool IsWinPeAddonRegistered = false)
{
    /// <summary>
    /// Gets whether Foundry can create WinPE media with the detected ADK state.
    /// </summary>
    public bool CanCreateMedia => IsInstalled && IsCompatible && IsWinPeAddonInstalled && IsWinPeAddonCompatible
        && ServicingState == AdkServicingState.Verified && (IsX64Available || IsArm64Available);

    /// <summary>Checks whether the selected target has the required WinPE and boot assets.</summary>
    public bool CanCreateMediaFor(WinPeArchitecture architecture) => CanCreateMedia && architecture switch
    {
        WinPeArchitecture.X64 => IsX64Available,
        WinPeArchitecture.Arm64 => IsArm64Available,
        _ => false
    };
}
