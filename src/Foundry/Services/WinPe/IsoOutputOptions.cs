namespace Foundry.Services.WinPe;

public sealed record IsoOutputOptions
{
    public string StagingDirectoryPath { get; init; } = string.Empty;
    public string OutputIsoPath { get; init; } = string.Empty;
    public string VolumeLabel { get; init; } = "FOUNDRY_WINPE";
    public string? WorkingDirectoryPath { get; init; }
    public string? AdkRootPath { get; init; }
    public WinPeArchitecture Architecture { get; init; } = WinPeArchitecture.X64;
    public WinPeSignatureMode SignatureMode { get; init; } = WinPeSignatureMode.Pca2011;
    public string WinPeLanguage { get; init; } = string.Empty;

    public IReadOnlyList<WinPeVendorSelection> DriverVendors { get; init; } = Array.Empty<WinPeVendorSelection>();
    public string DriverCatalogUri { get; init; } = WinPeDefaults.DefaultUnifiedCatalogUri;
    public string? CustomDriverDirectoryPath { get; init; }

    public bool ForceOverwriteOutput { get; init; } = true;
    public bool PreserveBuildWorkspace { get; init; }

    public bool RunPca2023RemediationWhenBootExUnsupported { get; init; }
    public string? Pca2023RemediationScriptPath { get; init; }
    public string? ExpertDeployConfigurationJson { get; init; }
}
