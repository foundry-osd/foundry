namespace Foundry.Core.Services.WinPe;

public sealed record WinPeToolPaths
{
    public string KitsRootPath { get; init; } = string.Empty;
    public string CopypePath { get; init; } = string.Empty;
    public string MakeWinPeMediaPath { get; init; } = string.Empty;
    public string DismPath { get; init; } = string.Empty;
    public string CmdPath { get; init; } = string.Empty;
    public string PowerShellPath { get; init; } = string.Empty;
}
