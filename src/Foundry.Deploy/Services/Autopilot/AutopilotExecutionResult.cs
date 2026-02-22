namespace Foundry.Deploy.Services.Autopilot;

public sealed record AutopilotExecutionResult
{
    public required bool IsSuccess { get; init; }
    public required bool OnlineRegistrationSucceeded { get; init; }
    public required bool DeferredCompletionPrepared { get; init; }
    public required string Message { get; init; }
    public required string WorkflowManifestPath { get; init; }
    public required string WorkflowScriptPath { get; init; }
    public required string TranscriptPath { get; init; }
    public string? DeferredScriptPath { get; init; }
    public string? SetupCompleteHookPath { get; init; }
}
