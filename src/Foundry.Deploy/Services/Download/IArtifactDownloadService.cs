namespace Foundry.Deploy.Services.Download;

public interface IArtifactDownloadService
{
    Task<ArtifactDownloadResult> DownloadAsync(
        string sourceUrl,
        string destinationPath,
        string? expectedHash = null,
        CancellationToken cancellationToken = default,
        IProgress<DownloadProgress>? progress = null);
}
