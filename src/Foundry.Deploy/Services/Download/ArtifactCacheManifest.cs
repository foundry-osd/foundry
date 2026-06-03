namespace Foundry.Deploy.Services.Download;

internal sealed record ArtifactCacheManifest
{
    public int Version { get; init; } = 1;
    public string? ArtifactKind { get; init; }
    public string? SourceUrl { get; init; }
    public required string HashAlgorithm { get; init; }
    public required string ExpectedHash { get; init; }
    public long? ExpectedSizeBytes { get; init; }
    public required long FileSizeBytes { get; init; }
    public required DateTimeOffset FileLastWriteTimeUtc { get; init; }
    public required DateTimeOffset ValidatedAtUtc { get; init; }
    public string ValidatedBy { get; init; } = "Foundry.Deploy";
}
