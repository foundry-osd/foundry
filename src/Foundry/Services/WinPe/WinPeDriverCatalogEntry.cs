namespace Foundry.Services.WinPe;

public sealed record WinPeDriverCatalogEntry
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public WinPeVendorSelection Vendor { get; init; }
    public WinPeDriverPackageRole PackageRole { get; init; } = WinPeDriverPackageRole.BaseDriverPack;
    public WinPeDriverFamily DriverFamily { get; init; } = WinPeDriverFamily.None;
    public WinPeArchitecture Architecture { get; init; }
    public string DownloadUri { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string Format { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public DateTimeOffset? ReleaseDate { get; init; }
}
