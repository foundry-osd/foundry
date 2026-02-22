namespace Foundry.Deploy.Services.Download;

public interface IArtifactDownloadService
{
    Task<ArtifactDownloadResult> DownloadAsync(
        string sourceUrl,
        string destinationPath,
        string? expectedSha256 = null,
        bool preferBits = true,
        CancellationToken cancellationToken = default);
}
