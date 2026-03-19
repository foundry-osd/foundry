namespace Foundry.Services.WinPe;

internal sealed record WinPePreparedMediaWorkspace
{
    public required WinPeBuildArtifact Artifact { get; init; }

    public required WinPeToolPaths Tools { get; init; }

    public bool UseBootEx { get; init; }
}
