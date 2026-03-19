namespace Foundry.Services.WinPe;

internal sealed record WinPeWorkspacePreparationResult
{
    public bool UseBootEx { get; init; }
}
