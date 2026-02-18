namespace Foundry.Services.WinPe;

public sealed record WinPeDriverCatalogOptions
{
    public string CatalogUri { get; init; } = WinPeDefaults.DefaultUnifiedCatalogUri;
    public WinPeArchitecture Architecture { get; init; } = WinPeArchitecture.X64;
    public WinPeVendorSelection Vendor { get; init; } = WinPeVendorSelection.Any;
    public string? SearchTerm { get; init; }
    public bool IncludePreviewDrivers { get; init; }
}
