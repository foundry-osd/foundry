namespace Foundry.Core.Services.WinPe;

public sealed record WinPeIsoMediaOptions
{
    public WinPeWorkspacePreparationResult? PreparedWorkspace { get; init; }
    public string OutputIsoPath { get; init; } = string.Empty;
    public string IsoTempDirectoryPath { get; init; } = string.Empty;
    public bool ForceOverwriteOutput { get; init; } = true;
}
