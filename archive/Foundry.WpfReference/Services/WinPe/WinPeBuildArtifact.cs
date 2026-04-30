namespace Foundry.Services.WinPe;

public sealed record WinPeBuildArtifact
{
    public string WorkingDirectoryPath { get; init; } = string.Empty;
    public string MediaDirectoryPath { get; init; } = string.Empty;
    public string BootWimPath { get; init; } = string.Empty;
    public string MountDirectoryPath { get; init; } = string.Empty;
    public string DriverWorkspacePath { get; init; } = string.Empty;
    public string LogsDirectoryPath { get; init; } = string.Empty;
    public string MakeWinPeMediaPath { get; init; } = string.Empty;
    public string DismPath { get; init; } = string.Empty;
    public WinPeArchitecture Architecture { get; init; }
    public WinPeSignatureMode SignatureMode { get; init; }
}
