namespace Foundry.Services.WinPe;

public sealed record WinPeDriverCatalogOptions
{
    public string CatalogUri { get; init; } = WinPeDefaults.DefaultUnifiedCatalogUri;
    public WinPeArchitecture Architecture { get; init; } = WinPeArchitecture.X64;
    public IReadOnlyList<WinPeVendorSelection> Vendors { get; init; } = Array.Empty<WinPeVendorSelection>();
    public string? SearchTerm { get; init; }
}
