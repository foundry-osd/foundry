namespace Foundry.Core.Services.Adk;

public sealed record AdkInstallationStatus(
    bool IsInstalled,
    bool IsCompatible,
    bool IsWinPeAddonInstalled,
    string? InstalledVersion,
    string? KitsRootPath,
    string RequiredVersionPolicy)
{
    public bool CanCreateMedia => IsInstalled && IsCompatible && IsWinPeAddonInstalled;
}
