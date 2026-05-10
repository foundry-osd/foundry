using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.WinPe;

public sealed record WinPeMountedImageAssetProvisioningOptions
{
    public string MountedImagePath { get; init; } = string.Empty;
    public WinPeArchitecture Architecture { get; init; } = WinPeArchitecture.X64;
    public string BootstrapScriptContent { get; init; } = string.Empty;
    public string CurlExecutableSourcePath { get; init; } = string.Empty;
    public string SevenZipSourceDirectoryPath { get; init; } = string.Empty;
    public string IanaWindowsTimeZoneMapJson { get; init; } = string.Empty;
    public string FoundryConnectConfigurationJson { get; init; } = string.Empty;
    public string ExpertDeployConfigurationJson { get; init; } = string.Empty;
    public byte[]? MediaSecretsKey { get; init; }
    public IReadOnlyList<FoundryConnectProvisionedAssetFile> FoundryConnectAssetFiles { get; init; } = [];
    public IReadOnlyList<AutopilotProfileSettings> AutopilotProfiles { get; init; } = [];
    public WinPeProvisioningSource ConnectProvisioningSource { get; init; } = WinPeProvisioningSource.Release;
    public WinPeProvisioningSource DeployProvisioningSource { get; init; } = WinPeProvisioningSource.Release;
}
