namespace Foundry.Core.Services.Adk;

/// <summary>
/// Describes the detected Windows ADK and WinPE add-on state.
/// </summary>
/// <param name="IsInstalled">Whether a Windows ADK installation was detected.</param>
/// <param name="IsCompatible">Whether the detected ADK version satisfies Foundry requirements.</param>
/// <param name="IsWinPeAddonInstalled">Whether the WinPE add-on is installed.</param>
/// <param name="InstalledVersion">The detected ADK version, when available.</param>
/// <param name="KitsRootPath">The detected Windows Kits root path, when available.</param>
/// <param name="RequiredVersionPolicy">The version policy used to evaluate compatibility.</param>
public sealed record AdkInstallationStatus(
    bool IsInstalled,
    bool IsCompatible,
    bool IsWinPeAddonInstalled,
    string? InstalledVersion,
    string? KitsRootPath,
    string RequiredVersionPolicy)
{
    /// <summary>
    /// Gets whether Foundry can create WinPE media with the detected ADK state.
    /// </summary>
    public bool CanCreateMedia => IsInstalled && IsCompatible && IsWinPeAddonInstalled;
}
