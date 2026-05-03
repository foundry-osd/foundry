namespace Foundry.Core.Services.WinPe;

public sealed record WinPeDriverCatalogOptions
{
    public string CatalogUri { get; init; } =
        "https://raw.githubusercontent.com/foundry-osd/catalog/refs/heads/main/Cache/WinPE/WinPE_Unified.xml";

    public WinPeArchitecture Architecture { get; init; } = WinPeArchitecture.X64;
    public IReadOnlyList<WinPeVendorSelection> Vendors { get; init; } = [];
    public string? RequiredWinPeReleaseId { get; init; } = "11";
    public string? SearchTerm { get; init; }
}
