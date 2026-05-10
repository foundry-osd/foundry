namespace Foundry.Core.Services.Configuration;

public sealed record NetworkMediaReadinessEvaluation
{
    public bool IsNetworkConfigurationReady { get; init; }
    public bool IsConnectProvisioningReady { get; init; }
    public bool AreRequiredSecretsReady { get; init; }
}
