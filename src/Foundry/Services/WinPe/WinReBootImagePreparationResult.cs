namespace Foundry.Services.WinPe;

internal sealed record WinReBootImagePreparationResult
{
    public required IReadOnlyList<WinReDependencyFile> DependencyFiles { get; init; }
}

internal sealed record WinReDependencyFile
{
    public required string FileName { get; init; }
    public required string StagedPath { get; init; }
}
