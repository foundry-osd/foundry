namespace Foundry.Services.WinPe;

internal sealed record WinPeMountedImageCustomizationRequest
{
    public required WinPeBuildArtifact Artifact { get; init; }

    public required WinPeToolPaths Tools { get; init; }

    public required IReadOnlyList<string> DriverDirectories { get; init; }

    public required string WinPeLanguage { get; init; }

    public string? ExpertDeployConfigurationJson { get; init; }
}
