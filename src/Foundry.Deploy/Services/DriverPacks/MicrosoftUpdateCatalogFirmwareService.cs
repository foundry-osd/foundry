using System.IO;
using Foundry.Deploy.Models;
using Foundry.Deploy.Services.Download;
using Foundry.Deploy.Services.System;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.Services.DriverPacks;

public sealed class MicrosoftUpdateCatalogFirmwareService : IMicrosoftUpdateCatalogFirmwareService
{
    private readonly IMicrosoftUpdateCatalogClient _catalogClient;
    private readonly IArtifactDownloadService _artifactDownloadService;
    private readonly IProcessRunner _processRunner;
    private readonly ILogger<MicrosoftUpdateCatalogFirmwareService> _logger;

    public MicrosoftUpdateCatalogFirmwareService(
        IMicrosoftUpdateCatalogClient catalogClient,
        IArtifactDownloadService artifactDownloadService,
        IProcessRunner processRunner,
        ILogger<MicrosoftUpdateCatalogFirmwareService> logger)
    {
        _catalogClient = catalogClient;
        _artifactDownloadService = artifactDownloadService;
        _processRunner = processRunner;
        _logger = logger;
    }

    public async Task<MicrosoftUpdateCatalogFirmwareResult> DownloadAsync(
        HardwareProfile hardwareProfile,
        string targetArchitecture,
        string rawDirectory,
        string extractedDirectory,
        CancellationToken cancellationToken = default,
        IProgress<double>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(hardwareProfile);
        ArgumentException.ThrowIfNullOrWhiteSpace(rawDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(extractedDirectory);

        string firmwareHardwareId = hardwareProfile.SystemFirmwareHardwareId.Trim();
        if (string.IsNullOrWhiteSpace(firmwareHardwareId))
        {
            return new MicrosoftUpdateCatalogFirmwareResult
            {
                Message = "System firmware hardware identifier is unavailable; skipping firmware update lookup."
            };
        }

        ResetDirectory(rawDirectory);
        ResetDirectory(extractedDirectory);
        progress?.Report(5d);

        if (!await _catalogClient.IsAvailableAsync(cancellationToken).ConfigureAwait(false))
        {
            return new MicrosoftUpdateCatalogFirmwareResult
            {
                DownloadedDirectory = rawDirectory,
                ExtractedDirectory = extractedDirectory,
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
                ExtractedDirectory = extractedDirectory,
                Message = $"No firmware update was found in Microsoft Update Catalog for firmware id '{firmwareHardwareId}'."
            };
        }

        progress?.Report(35d);
        IReadOnlyList<string> downloadUrls = await _catalogClient
            .GetDownloadUrlsAsync(update.UpdateId, cancellationToken)
            .ConfigureAwait(false);

        string? selectedUrl = MicrosoftUpdateCatalogSupport.SelectPreferredCabUrl(downloadUrls, targetArchitecture);
        if (string.IsNullOrWhiteSpace(selectedUrl))
        {
            return new MicrosoftUpdateCatalogFirmwareResult
            {
                DownloadedDirectory = rawDirectory,
                ExtractedDirectory = extractedDirectory,
                UpdateId = update.UpdateId,
                Title = update.Title,
                Message = $"Firmware update '{update.Title}' was found, but no CAB payload was available for download."
            };
        }

        string updateDirectory = Path.Combine(rawDirectory, MicrosoftUpdateCatalogSupport.SanitizePathSegment(update.UpdateId));
        Directory.CreateDirectory(updateDirectory);

        string fileName = MicrosoftUpdateCatalogSupport.ResolveFileNameFromUrl(selectedUrl);
        string destinationPath = Path.Combine(updateDirectory, fileName);

        progress?.Report(50d);
        await _artifactDownloadService
            .DownloadAsync(selectedUrl, destinationPath, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        progress?.Report(75d);
        int infCount = await ExpandAsync(rawDirectory, extractedDirectory, cancellationToken, progress).ConfigureAwait(false);
        if (infCount == 0)
        {
            throw new InvalidOperationException(
                $"Firmware update '{update.Title}' was downloaded but no INF files were found after expansion.");
        }

        _logger.LogInformation(
            "Firmware update downloaded and expanded. UpdateId={UpdateId}, Title={Title}, InfCount={InfCount}",
            update.UpdateId,
            update.Title,
            infCount);

        return new MicrosoftUpdateCatalogFirmwareResult
        {
            IsUpdateAvailable = true,
            DownloadedDirectory = rawDirectory,
            ExtractedDirectory = extractedDirectory,
            UpdateId = update.UpdateId,
            Title = update.Title,
            InfCount = infCount,
            Message = $"Firmware update prepared: {update.Title} ({infCount} INF files)."
        };
    }

    private async Task<int> ExpandAsync(
        string sourceDirectory,
        string destinationDirectory,
        CancellationToken cancellationToken,
        IProgress<double>? progress)
    {
        string[] cabFiles = Directory
            .EnumerateFiles(sourceDirectory, "*.cab", SearchOption.AllDirectories)
            .ToArray();

        if (cabFiles.Length == 0)
        {
            return 0;
        }

        for (int index = 0; index < cabFiles.Length; index++)
        {
            string cabPath = cabFiles[index];
            string folderName = ResolveExpandedFolderName(cabPath, sourceDirectory);
            string cabDestination = Path.Combine(destinationDirectory, MicrosoftUpdateCatalogSupport.SanitizePathSegment(folderName));
            Directory.CreateDirectory(cabDestination);

            ProcessExecutionResult execution = await _processRunner
                .RunAsync(
                    "expand.exe",
                    [cabPath, "-F:*", cabDestination],
                    destinationDirectory,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!execution.IsSuccess)
            {
                throw new InvalidOperationException(
                    "Firmware payload expansion failed." + Environment.NewLine +
                    $"ExitCode: {execution.ExitCode}" + Environment.NewLine +
                    execution.StandardOutput + Environment.NewLine +
                    execution.StandardError);
            }

            double percent = 75d + (double)(index + 1) / cabFiles.Length * 25d;
            progress?.Report(percent);
        }

        return Directory.EnumerateFiles(destinationDirectory, "*.inf", SearchOption.AllDirectories).Count();
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

    private static void ResetDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }

        Directory.CreateDirectory(path);
    }
}
