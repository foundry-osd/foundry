using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.WinPe;

public sealed record UsbOutputOptions
{
    public string StagingDirectoryPath { get; init; } = string.Empty;
    public int? TargetDiskNumber { get; init; }
    public string ExpectedDiskFriendlyName { get; init; } = string.Empty;
    public string ExpectedDiskSerialNumber { get; init; } = string.Empty;
    public string ExpectedDiskUniqueId { get; init; } = string.Empty;
    public UsbPartitionStyle PartitionStyle { get; init; } = UsbPartitionStyle.Gpt;
    public UsbFormatMode FormatMode { get; init; } = UsbFormatMode.Quick;
    public string? WorkingDirectoryPath { get; init; }
    public string? AdkRootPath { get; init; }
    public WinPeArchitecture Architecture { get; init; } = WinPeArchitecture.X64;
    public WinPeSignatureMode SignatureMode { get; init; } = WinPeSignatureMode.Pca2011;
    public WinPeBootImageSource BootImageSource { get; init; } = WinPeBootImageSource.WinPe;
    public string WinPeLanguage { get; init; } = string.Empty;
    public IReadOnlyList<WinPeVendorSelection> DriverVendors { get; init; } = [];
    public string DriverCatalogUri { get; init; } = string.Empty;
    public string? CustomDriverDirectoryPath { get; init; }
    public string? FoundryConnectConfigurationJson { get; init; }
    public IReadOnlyList<FoundryConnectProvisionedAssetFile> FoundryConnectAssetFiles { get; init; } = [];
    public string? ExpertDeployConfigurationJson { get; init; }
    public IReadOnlyList<AutopilotProfileSettings> AutopilotProfiles { get; init; } = [];
    public bool PreserveBuildWorkspace { get; init; }
}
