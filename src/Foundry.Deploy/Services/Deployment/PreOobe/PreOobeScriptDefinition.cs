namespace Foundry.Deploy.Services.Deployment.PreOobe;

public sealed record PreOobeScriptDefinition
{
    public required string Id { get; init; }
    public required string FileName { get; init; }
    public required string ResourceName { get; init; }
    public required PreOobeScriptPriority Priority { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = [];
}
