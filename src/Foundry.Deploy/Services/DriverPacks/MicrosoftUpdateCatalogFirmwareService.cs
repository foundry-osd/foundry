// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using Foundry.Deploy.Models;
using Foundry.Deploy.Services.Download;
using Foundry.Deploy.Services.System;
using Foundry.Utilities.IO;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.Services.DriverPacks;

public sealed class MicrosoftUpdateCatalogFirmwareService : IMicrosoftUpdateCatalogFirmwareService
{
    private readonly IArchiveExtractionService _archiveExtractionService;
    private readonly IMicrosoftUpdateCatalogClient _catalogClient;
    private readonly IArtifactDownloadService _artifactDownloadService;
    private readonly ILogger<MicrosoftUpdateCatalogFirmwareService> _logger;

    public MicrosoftUpdateCatalogFirmwareService(
        IArchiveExtractionService archiveExtractionService,
        IMicrosoftUpdateCatalogClient catalogClient,
        IArtifactDownloadService artifactDownloadService,
        ILogger<MicrosoftUpdateCatalogFirmwareService> logger)
    {
        _archiveExtractionService = archiveExtractionService;
        _catalogClient = catalogClient;
        _artifactDownloadService = artifactDownloadService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<MicrosoftUpdateCatalogFirmwareResult> DownloadAsync(
        HardwareProfile hardwareProfile,
        string targetArchitecture,
        string rawDirectory,
        Func<long, string, string> resolveCacheDirectory,
        CancellationToken cancellationToken = default,
        IProgress<double>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(hardwareProfile);
        ArgumentException.ThrowIfNullOrWhiteSpace(rawDirectory);
        ArgumentNullException.ThrowIfNull(resolveCacheDirectory);

        string firmwareHardwareId = hardwareProfile.SystemFirmwareHardwareId.Trim();
        if (string.IsNullOrWhiteSpace(firmwareHardwareId))
        {
            return new MicrosoftUpdateCatalogFirmwareResult
            {
                Message = "System firmware hardware identifier is unavailable; skipping firmware update lookup."
            };
        }

        DirectoryOperations.Recreate(rawDirectory);
        progress?.Report(5d);

        if (!await _catalogClient.IsAvailableAsync(cancellationToken).ConfigureAwait(false))
        {
            return new MicrosoftUpdateCatalogFirmwareResult
            {
                DownloadedDirectory = rawDirectory,
                Message = "Microsoft Update Catalog is not reachable; skipping firmware update."
            };
        }

        progress?.Report(20d);
        IReadOnlyList<MicrosoftUpdateCatalogUpdate> updates = await _catalogClient
            .SearchAsync(firmwareHardwareId, descending: true, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        MicrosoftUpdateCatalogUpdate? update = updates.FirstOrDefault();
        if (update is null)
        {
            return new MicrosoftUpdateCatalogFirmwareResult
            {
                DownloadedDirectory = rawDirectory,
                Message = $"No firmware update was found in Microsoft Update Catalog for firmware id '{firmwareHardwareId}'."
            };
        }

        progress?.Report(35d);
        IReadOnlyList<MicrosoftUpdateCatalogDownload> downloads = await _catalogClient
            .GetDownloadsAsync(update.UpdateId, cancellationToken)
            .ConfigureAwait(false);

        MicrosoftUpdateCatalogDownload? selectedDownload = MicrosoftUpdateCatalogSupport.SelectPreferredCab(downloads, targetArchitecture);
        if (selectedDownload is null)
        {
            return new MicrosoftUpdateCatalogFirmwareResult
            {
                DownloadedDirectory = rawDirectory,
                UpdateId = update.UpdateId,
                Title = update.Title,
                Message = $"Firmware update '{update.Title}' was found, but no CAB payload was available for download."
            };
        }

        string updateDirectory = Path.Combine(rawDirectory, MicrosoftUpdateCatalogSupport.SanitizePathSegment(update.UpdateId));
        Directory.CreateDirectory(updateDirectory);

        string fileName = ResolveFileName(selectedDownload);
        string destinationPath = Path.Combine(updateDirectory, fileName);

        progress?.Report(50d);
        ArtifactDownloadResult download = await DownloadToStagingAsync(update, selectedDownload, destinationPath, resolveCacheDirectory, cancellationToken)
            .ConfigureAwait(false);
        if (!File.Exists(destinationPath))
        {
            throw new FileNotFoundException("The selected firmware payload is unavailable.", destinationPath);
        }
        progress?.Report(100d);

        _logger.LogInformation(
            "Firmware update acquired. UpdateId={UpdateId}, Title={Title}, Downloaded={Downloaded}",
            update.UpdateId,
            update.Title,
            download.Downloaded);

        return new MicrosoftUpdateCatalogFirmwareResult
        {
            IsUpdateAvailable = true,
            DownloadedDirectory = rawDirectory,
            DownloadedCount = download.Downloaded ? 1 : 0,
            ReusedCount = download.Downloaded ? 0 : 1,
            UpdateId = update.UpdateId,
            Title = update.Title,
            Message = $"Firmware update {(download.Downloaded ? "downloaded" : "resolved from cache")}: {update.Title}."
        };
    }

    /// <inheritdoc />
    public async Task<int> ExtractAsync(
        string sourceDirectory,
        string destinationDirectory,
        CancellationToken cancellationToken = default,
        IProgress<double>? progress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        cancellationToken.ThrowIfCancellationRequested();
        string[] cabFiles = Directory
            .EnumerateFiles(sourceDirectory, "*.cab", SearchOption.AllDirectories)
            .ToArray();

        if (cabFiles.Length == 0)
        {
            throw new InvalidOperationException("The selected firmware payload does not contain any CAB files.");
        }

        DirectoryOperations.Recreate(destinationDirectory);

        for (int index = 0; index < cabFiles.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string cabPath = cabFiles[index];
            string folderName = ResolveExpandedFolderName(cabPath, sourceDirectory);
            string cabDestination = Path.Combine(destinationDirectory, MicrosoftUpdateCatalogSupport.SanitizePathSegment(folderName));
            Directory.CreateDirectory(cabDestination);

            double rangeStart = (double)index / cabFiles.Length * 100d;
            double rangeEnd = (double)(index + 1) / cabFiles.Length * 100d;
            await _archiveExtractionService
                .ExtractWithSevenZipAsync(
                    cabPath,
                    cabDestination,
                    destinationDirectory,
                    CancellationToken.None,
                    CreateMappedProgress(progress, rangeStart, rangeEnd))
                .ConfigureAwait(false);
            // Await the extraction process before honoring cancellation or starting another archive.
            cancellationToken.ThrowIfCancellationRequested();
        }

        int infCount = Directory.EnumerateFiles(destinationDirectory, "*.inf", SearchOption.AllDirectories).Count();
        if (infCount == 0)
        {
            throw new InvalidOperationException("The extracted firmware payload does not contain any INF files.");
        }

        return infCount;
    }

    private async Task<ArtifactDownloadResult> DownloadToStagingAsync(
        MicrosoftUpdateCatalogUpdate update,
        MicrosoftUpdateCatalogDownload download,
        string destinationPath,
        Func<long, string, string> resolveCacheDirectory,
        CancellationToken cancellationToken)
    {
        string expectedHash = MicrosoftUpdateCatalogSupport.ResolvePreferredHash(download);
        if (string.IsNullOrWhiteSpace(expectedHash))
        {
            return await _artifactDownloadService
                .DownloadAsync(download.DownloadUrl, destinationPath, artifactKind: "MicrosoftUpdateCatalogFirmware", cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        string relativePath = Path.Combine(
            MicrosoftUpdateCatalogSupport.SanitizePathSegment(update.UpdateId),
            ResolveFileName(download));
        string cachePath = Path.Combine(resolveCacheDirectory(update.SizeInBytes, relativePath), relativePath);

        ArtifactDownloadResult result = await _artifactDownloadService
            .DownloadAsync(
                download.DownloadUrl,
                cachePath,
                expectedHash: expectedHash,
                artifactKind: "MicrosoftUpdateCatalogFirmware",
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        CopyFile(cachePath, destinationPath);
        return result;
    }

    private static string ResolveFileName(MicrosoftUpdateCatalogDownload download)
    {
        return string.IsNullOrWhiteSpace(download.FileName)
            ? MicrosoftUpdateCatalogSupport.ResolveFileNameFromUrl(download.DownloadUrl)
            : MicrosoftUpdateCatalogSupport.SanitizePathSegment(download.FileName);
    }

    private static void CopyFile(string sourcePath, string destinationPath)
    {
        string? destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        File.Copy(sourcePath, destinationPath, overwrite: true);
    }

    private static string ResolveExpandedFolderName(string cabPath, string sourceDirectory)
    {
        string parentFolder = Path.GetFileName(Path.GetDirectoryName(cabPath) ?? string.Empty);
        string sourceFolder = Path.GetFileName(sourceDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        return !string.IsNullOrWhiteSpace(parentFolder) &&
               !parentFolder.Equals(sourceFolder, StringComparison.OrdinalIgnoreCase)
            ? parentFolder
            : Path.GetFileNameWithoutExtension(cabPath);
    }

    private static IProgress<double>? CreateMappedProgress(IProgress<double>? progress, double start, double end)
    {
        if (progress is null)
        {
            return null;
        }

        return new CatalogExtractionProgress(progress, start, end);
    }
}
