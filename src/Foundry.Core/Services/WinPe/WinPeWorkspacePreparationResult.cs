namespace Foundry.Core.Services.WinPe;

public sealed record WinPeWorkspacePreparationResult
{
    public required WinPeBuildArtifact Artifact { get; init; }
    public required WinPeToolPaths Tools { get; init; }
    public bool UseBootEx { get; init; }
}
