namespace Foundry.Services.WinPe;

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
    public string WinPeLanguage { get; init; } = string.Empty;

    public IReadOnlyList<WinPeVendorSelection> DriverVendors { get; init; } = Array.Empty<WinPeVendorSelection>();
    public string DriverCatalogUri { get; init; } = WinPeDefaults.DefaultUnifiedCatalogUri;
    public string? CustomDriverDirectoryPath { get; init; }

    public bool RunPca2023RemediationWhenBootExUnsupported { get; init; }
    public string? Pca2023RemediationScriptPath { get; init; }

    public bool PreserveBuildWorkspace { get; init; }
}
