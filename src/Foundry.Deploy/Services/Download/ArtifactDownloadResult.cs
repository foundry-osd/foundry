namespace Foundry.Deploy.Services.Download;

public sealed record ArtifactDownloadResult
{
    public required string DestinationPath { get; init; }
    public required bool Downloaded { get; init; }
    public required string Method { get; init; }
    public long SizeBytes { get; init; }
}
