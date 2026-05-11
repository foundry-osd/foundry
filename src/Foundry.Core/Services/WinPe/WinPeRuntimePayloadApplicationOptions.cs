namespace Foundry.Core.Services.WinPe;

public sealed record WinPeRuntimePayloadApplicationOptions
{
    public bool IsEnabled { get; init; }
    public WinPeProvisioningSource ProvisioningSource { get; init; } = WinPeProvisioningSource.Debug;
    public string ArchivePath { get; init; } = string.Empty;
    public string ProjectPath { get; init; } = string.Empty;
}
