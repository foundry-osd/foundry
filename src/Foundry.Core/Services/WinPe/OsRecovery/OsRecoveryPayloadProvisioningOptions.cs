namespace Foundry.Core.Services.WinPe.OsRecovery;

public sealed record OsRecoveryPayloadProvisioningOptions
{
    public const long DefaultManagedPayloadSizeBytes = 256L * 1024L * 1024L;

    public string MountedImagePath { get; init; } = string.Empty;
    public string WorkingDirectoryPath { get; init; } = string.Empty;
    public WinPeArchitecture Architecture { get; init; } = WinPeArchitecture.X64;
    public string FoundryConnectConfigurationJson { get; init; } = string.Empty;
    public string DeployConfigurationJson { get; init; } = string.Empty;
    public string IanaWindowsTimeZoneMapJson { get; init; } = string.Empty;
    public string SevenZipSourceDirectoryPath { get; init; } = string.Empty;
    public WinPeRuntimePayloadApplicationOptions Connect { get; init; } = new();
    public IReadOnlyList<OsRecoveryBootMenuLocalization> BootMenuLocalizations { get; init; } = [];
    public long MaxManagedPayloadSizeBytes { get; init; } = DefaultManagedPayloadSizeBytes;
}
