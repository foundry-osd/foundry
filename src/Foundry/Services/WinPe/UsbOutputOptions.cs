namespace Foundry.Services.WinPe;

public sealed record UsbOutputOptions
{
    public string StagingDirectoryPath { get; init; } = string.Empty;
    public string TargetDriveLetter { get; init; } = string.Empty;
    public int? TargetDiskNumber { get; init; }
    public string ExpectedDiskFriendlyName { get; init; } = string.Empty;
    public string ExpectedDiskSerialNumber { get; init; } = string.Empty;
    public string ExpectedDiskUniqueId { get; init; } = string.Empty;
    public string ConfirmationCode { get; init; } = string.Empty;
    public string ConfirmationCodeRepeat { get; init; } = string.Empty;

    public UsbPartitionStyle PartitionStyle { get; init; } = UsbPartitionStyle.Gpt;
    public string? WorkingDirectoryPath { get; init; }
    public string? AdkRootPath { get; init; }
    public WinPeArchitecture Architecture { get; init; } = WinPeArchitecture.X64;
    public WinPeSignatureMode SignatureMode { get; init; } = WinPeSignatureMode.Pca2023;

    public WinPeVendorSelection Vendor { get; init; } = WinPeVendorSelection.Any;
    public string DriverCatalogUri { get; init; } = WinPeDefaults.DefaultUnifiedCatalogUri;
    public bool IncludeDrivers { get; init; } = true;
    public bool IncludePreviewDrivers { get; init; }

    public string? StartupBootstrapScriptPath { get; init; }
    public string? StartupBootstrapScriptContent { get; init; }
    public bool RunPca2023RemediationWhenBootExUnsupported { get; init; }
    public string? Pca2023RemediationScriptPath { get; init; }

    public bool PreserveBuildWorkspace { get; init; }
}
