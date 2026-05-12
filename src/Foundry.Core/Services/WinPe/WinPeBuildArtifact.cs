namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Contains paths produced by WinPE workspace creation and consumed by later customization/media stages.
/// </summary>
public sealed record WinPeBuildArtifact
{
    /// <summary>
    /// Gets the root WinPE working directory.
    /// </summary>
    public string WorkingDirectoryPath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the ADK media directory inside the workspace.
    /// </summary>
    public string MediaDirectoryPath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the boot.wim path to mount or copy.
    /// </summary>
    public string BootWimPath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the mount directory for boot image customization.
    /// </summary>
    public string MountDirectoryPath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the driver workspace used for downloads and extraction.
    /// </summary>
    public string DriverWorkspacePath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the log directory used by ADK and DISM operations.
    /// </summary>
    public string LogsDirectoryPath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the MakeWinPEMedia.cmd path.
    /// </summary>
    public string MakeWinPeMediaPath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the DISM executable path.
    /// </summary>
    public string DismPath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the architecture of the generated WinPE workspace.
    /// </summary>
    public WinPeArchitecture Architecture { get; init; }

    /// <summary>
    /// Gets the signature mode selected for the resulting media.
    /// </summary>
    public WinPeSignatureMode SignatureMode { get; init; }
}
