namespace Foundry.Deploy.Services.Download;

public readonly record struct DownloadProgress(long BytesDownloaded, long? TotalBytes);
