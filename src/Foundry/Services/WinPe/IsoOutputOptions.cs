namespace Foundry.Services.WinPe;

public sealed record IsoOutputOptions
{
    public string StagingDirectoryPath { get; init; } = string.Empty;
    public string OutputIsoPath { get; init; } = string.Empty;
    public string VolumeLabel { get; init; } = "FOUNDRY_WINPE";
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

    public bool ForceOverwriteOutput { get; init; } = true;
    public bool PreserveBuildWorkspace { get; init; }

    public bool RunPca2023RemediationWhenBootExUnsupported { get; init; }
    public string? Pca2023RemediationScriptPath { get; init; }
}
