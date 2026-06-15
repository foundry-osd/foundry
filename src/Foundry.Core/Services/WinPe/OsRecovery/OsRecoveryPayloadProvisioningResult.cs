namespace Foundry.Core.Services.WinPe.OsRecovery;

public sealed record OsRecoveryPayloadProvisioningResult
{
    public required string BootMenuConfigurationXml { get; init; }
    public required long ManagedPayloadSizeBytes { get; init; }
}
