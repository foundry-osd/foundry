namespace Foundry.Deploy.Services.System;

public interface IArchiveExtractionService
{
    Task ExtractWithSevenZipAsync(
        string archivePath,
        string extractedPath,
        string workingDirectory,
        CancellationToken cancellationToken = default,
        IProgress<double>? progress = null);
}
