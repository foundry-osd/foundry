namespace Foundry.Deploy.Services.Download;

public interface IArtifactDownloadService
{
    Task<ArtifactDownloadResult> DownloadAsync(
        string sourceUrl,
        string destinationPath,
        string? expectedHash = null,
        bool preferBits = true,
        CancellationToken cancellationToken = default);
}
