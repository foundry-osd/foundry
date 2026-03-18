using Foundry.Deploy.Services.Runtime;

namespace Foundry.Deploy.Services.Startup;

public sealed record DeploymentStartupRequest
{
    public required DeploymentRuntimeContext RuntimeContext { get; init; }
    public required bool IsDebugSafeMode { get; init; }
    public required string FallbackComputerName { get; init; }
}
