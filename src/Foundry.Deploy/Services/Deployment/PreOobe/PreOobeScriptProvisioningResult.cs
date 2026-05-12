namespace Foundry.Deploy.Services.Deployment.PreOobe;

public sealed record PreOobeScriptProvisioningResult
{
    public required string SetupCompletePath { get; init; }
    public required string RunnerPath { get; init; }
    public required string ManifestPath { get; init; }
    public required IReadOnlyList<string> StagedScriptPaths { get; init; }
}
