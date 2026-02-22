namespace Foundry.Deploy.Services.Autopilot;

public sealed record AutopilotExecutionResult
{
    public required bool IsSuccess { get; init; }
    public required string Message { get; init; }
    public required string WorkflowManifestPath { get; init; }
    public required string WorkflowScriptPath { get; init; }
    public required string TranscriptPath { get; init; }
}
