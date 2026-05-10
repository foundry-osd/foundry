namespace Foundry.Core.Services.WinPe;

public sealed record WinReBootImagePreparationResult
{
    public required IReadOnlyList<WinReDependencyFile> DependencyFiles { get; init; }
}

public sealed record WinReDependencyFile
{
    public required string FileName { get; init; }
    public required string StagedPath { get; init; }
}
