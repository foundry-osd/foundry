namespace Foundry.Deploy.Services.DriverPacks;

public sealed record DriverPackPreparationResult
{
    public required string ArchivePath { get; init; }
    public string? ExtractedDirectoryPath { get; init; }
    public required bool RequiresDeferredInstall { get; init; }
    public required string Message { get; init; }
}
