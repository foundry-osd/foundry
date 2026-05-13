namespace Foundry.Deploy.Services.System;

/// <summary>
/// Extracts archive payloads during WinPE deployment using the 7-Zip tooling provisioned into the boot image.
/// </summary>
public interface IArchiveExtractionService
{
    /// <summary>
    /// Extracts an archive into the specified directory and reports 7-Zip progress when available.
    /// </summary>
    /// <param name="archivePath">The archive file to extract.</param>
    /// <param name="extractedPath">The destination directory for extracted files.</param>
    /// <param name="workingDirectory">The working directory used to launch the 7-Zip process.</param>
    /// <param name="cancellationToken">A token used to cancel process execution.</param>
    /// <param name="progress">Optional progress receiver for deployment step reporting.</param>
    Task ExtractWithSevenZipAsync(
        string archivePath,
        string extractedPath,
        string workingDirectory,
        CancellationToken cancellationToken = default,
        IProgress<double>? progress = null);
}
