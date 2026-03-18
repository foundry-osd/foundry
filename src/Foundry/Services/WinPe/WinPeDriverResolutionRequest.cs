namespace Foundry.Services.WinPe;

internal sealed record WinPeDriverResolutionRequest
{
    public required string CatalogUri { get; init; }

    public required WinPeArchitecture Architecture { get; init; }

    public required IReadOnlyList<WinPeVendorSelection> DriverVendors { get; init; }

    public string? CustomDriverDirectoryPath { get; init; }

    public required WinPeBuildArtifact Artifact { get; init; }
}
