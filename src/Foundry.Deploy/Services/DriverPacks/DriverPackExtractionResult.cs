namespace Foundry.Deploy.Services.DriverPacks;

public sealed record DriverPackExtractionResult
{
    public required DriverPackExecutionPlan ExecutionPlan { get; init; }
    public string? ExtractedDirectoryPath { get; init; }
    public required int InfCount { get; init; }
    public required string Message { get; init; }
}
