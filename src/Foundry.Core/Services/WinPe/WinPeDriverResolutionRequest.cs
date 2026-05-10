namespace Foundry.Core.Services.WinPe;

public sealed record WinPeDriverResolutionRequest
{
    public required string CatalogUri { get; init; }
    public required WinPeArchitecture Architecture { get; init; }
    public required WinPeBootImageSource BootImageSource { get; init; }
    public required IReadOnlyList<WinPeVendorSelection> DriverVendors { get; init; }
    public string? CustomDriverDirectoryPath { get; init; }
    public required WinPeBuildArtifact Artifact { get; init; }
    public IProgress<WinPeDownloadProgress>? DownloadProgress { get; init; }
}
