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

public sealed class MicrosoftUpdateCatalogDriverService : IMicrosoftUpdateCatalogDriverService
{
    private const string FirmwareClassGuid = "{f2e7dd72-6468-4e36-b6f1-6488f42c1b52}";
    private static readonly string[] CriticalPnpClasses =
    [
        "DiskDrive",
        "Net",
        "SCSIAdapter"
    ];

    private readonly IArchiveExtractionService _archiveExtractionService;
    private readonly IMicrosoftUpdateCatalogClient _catalogClient;
    private readonly IArtifactDownloadService _artifactDownloadService;
    private readonly ILogger<MicrosoftUpdateCatalogDriverService> _logger;

    public MicrosoftUpdateCatalogDriverService(
        IArchiveExtractionService archiveExtractionService,
        IMicrosoftUpdateCatalogClient catalogClient,
        IArtifactDownloadService artifactDownloadService,
        ILogger<MicrosoftUpdateCatalogDriverService> logger)
    {
        _archiveExtractionService = archiveExtractionService;
        _catalogClient = catalogClient;
        _artifactDownloadService = artifactDownloadService;
        _logger = logger;
    }

    public async Task<MicrosoftUpdateCatalogDriverResult> DownloadAsync(
        HardwareProfile hardwareProfile,
        OperatingSystemCatalogItem operatingSystem,
        string destinationDirectory,
        Func<long, string, string> resolveCacheDirectory,
        CancellationToken cancellationToken = default,
        IProgress<double>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(hardwareProfile);
        ArgumentNullException.ThrowIfNull(operatingSystem);

        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            throw new ArgumentException("Destination directory is required.", nameof(destinationDirectory));
        }

        ArgumentNullException.ThrowIfNull(resolveCacheDirectory);

        DirectoryOperations.Recreate(destinationDirectory);
        progress?.Report(5d);

        DriverSearchTarget[] searchTargets = BuildSearchTargets(hardwareProfile);
        if (searchTargets.Length == 0)
        {
            progress?.Report(100d);
            return new MicrosoftUpdateCatalogDriverResult
            {
                DestinationDirectory = destinationDirectory,
                IsPayloadAvailable = false,
                InfCount = 0,
                DownloadedDrivers = Array.Empty<MicrosoftUpdateCatalogDownloadedDriver>(),
                Message = "No eligible critical Plug and Play devices (DiskDrive, Net, SCSIAdapter) were found for Microsoft Update Catalog driver lookup."
            };
        }

        if (!await _catalogClient.IsAvailableAsync(cancellationToken).ConfigureAwait(false))
        {
            progress?.Report(100d);
            return new MicrosoftUpdateCatalogDriverResult
            {
                DestinationDirectory = destinationDirectory,
                IsPayloadAvailable = false,
                InfCount = 0,
                DownloadedDrivers = Array.Empty<MicrosoftUpdateCatalogDownloadedDriver>(),
                Message = "Microsoft Update Catalog is not reachable; skipping driver lookup."
            };
        }

        progress?.Report(15d);
        string[] releaseSearchOrder = MicrosoftUpdateCatalogSupport.BuildReleaseSearchOrder(operatingSystem.ReleaseId);
        var matchedUpdates = new Dictionary<string, CatalogDownloadCandidate>(StringComparer.OrdinalIgnoreCase);

        for (int index = 0; index < searchTargets.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            DriverSearchTarget searchTarget = searchTargets[index];
            CatalogDownloadCandidate? candidate = await FindCandidateAsync(
                    searchTarget,
                    releaseSearchOrder,
                    operatingSystem.Architecture,
                    cancellationToken)
                .ConfigureAwait(false);

            if (candidate is not null)
            {
                matchedUpdates.TryAdd(candidate.Update.UpdateId, candidate);
            }

            progress?.Report(15d + (double)(index + 1) / searchTargets.Length * 45d);
        }

        if (matchedUpdates.Count == 0)
        {
            progress?.Report(100d);
            return new MicrosoftUpdateCatalogDriverResult
            {
                DestinationDirectory = destinationDirectory,
                IsPayloadAvailable = false,
                InfCount = 0,
                DownloadedDrivers = Array.Empty<MicrosoftUpdateCatalogDownloadedDriver>(),
                Message = "Microsoft Update Catalog did not return any applicable driver payloads for the detected critical devices (DiskDrive, Net, SCSIAdapter)."
            };
        }

        int downloadIndex = 0;
        int downloadedCount = 0;
        List<MicrosoftUpdateCatalogDownloadedDriver> downloadedDrivers = [];
        foreach (CatalogDownloadCandidate candidate in matchedUpdates.Values.OrderBy(static item => item.Update.Title, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            ArtifactDownloadResult payload = await DownloadPayloadAsync(
                    candidate,
                    destinationDirectory,
                    resolveCacheDirectory,
                    cancellationToken)
                .ConfigureAwait(false);

            if (payload.Downloaded)
            {
                downloadedCount++;
            }

            downloadedDrivers.Add(new MicrosoftUpdateCatalogDownloadedDriver
            {
                UpdateId = candidate.Update.UpdateId,
                Title = candidate.Update.Title,
                Version = candidate.Update.Version,
                Size = candidate.Update.Size,
                DownloadUrl = candidate.Download.DownloadUrl,
                FilePath = payload.DestinationPath
            });

            downloadIndex++;
            progress?.Report(60d + (double)downloadIndex / matchedUpdates.Count * 40d);
        }

        int cabCount = downloadedDrivers.Count;
        progress?.Report(100d);

        return new MicrosoftUpdateCatalogDriverResult
        {
            DestinationDirectory = destinationDirectory,
            IsPayloadAvailable = cabCount > 0,
            DownloadedCount = downloadedCount,
            ReusedCount = cabCount - downloadedCount,
            DownloadedDrivers = downloadedDrivers,
            Message = $"Microsoft Update Catalog payload resolved: {cabCount} CAB files across {matchedUpdates.Count} updates."
        };
    }

    public async Task<MicrosoftUpdateCatalogDriverResult> ExpandAsync(
        IReadOnlyList<string> sourcePaths,
        string destinationDirectory,
        CancellationToken cancellationToken = default,
        IProgress<double>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(sourcePaths);

        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            throw new ArgumentException("Destination directory is required.", nameof(destinationDirectory));
        }

        progress?.Report(5d);

        string[] cabFiles = sourcePaths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (cabFiles.Length == 0)
        {
            throw new InvalidOperationException("No selected Microsoft Update Catalog driver payload is available for extraction.");
        }

        DirectoryOperations.Recreate(destinationDirectory);

        for (int index = 0; index < cabFiles.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string cabPath = cabFiles[index];
            if (!File.Exists(cabPath))
            {
                throw new FileNotFoundException("Selected Microsoft Update Catalog driver payload was not found.", cabPath);
            }

            string folderName = $"{index}-{Path.GetFileNameWithoutExtension(cabPath)}";
            string cabDestination = Path.Combine(destinationDirectory, MicrosoftUpdateCatalogSupport.SanitizePathSegment(folderName));
            Directory.CreateDirectory(cabDestination);

            double rangeStart = 10d + (double)index / cabFiles.Length * 85d;
            double rangeEnd = 10d + (double)(index + 1) / cabFiles.Length * 85d;
            await _archiveExtractionService
                .ExtractWithSevenZipAsync(
                    cabPath,
                    cabDestination,
                    destinationDirectory,
                    cancellationToken,
                    CreateMappedProgress(progress, rangeStart, rangeEnd))
                .ConfigureAwait(false);
            if (!Directory.EnumerateFiles(cabDestination, "*.inf", SearchOption.AllDirectories).Any())
            {
                throw new InvalidOperationException($"The selected Microsoft Update Catalog driver payload '{Path.GetFileName(cabPath)}' does not contain any INF files.");
            }
        }

        int infCount = Directory
            .EnumerateFiles(destinationDirectory, "*.inf", SearchOption.AllDirectories)
            .Count();
        progress?.Report(100d);

        return new MicrosoftUpdateCatalogDriverResult
        {
            DestinationDirectory = destinationDirectory,
            IsPayloadAvailable = true,
            InfCount = infCount,
            DownloadedDrivers = Array.Empty<MicrosoftUpdateCatalogDownloadedDriver>(),
            Message = $"Microsoft Update Catalog payload expanded: {infCount} INF files from {cabFiles.Length} CAB files."
        };
    }

    private async Task<CatalogDownloadCandidate?> FindCandidateAsync(
        DriverSearchTarget searchTarget,
        IReadOnlyList<string> releaseSearchOrder,
        string targetArchitecture,
        CancellationToken cancellationToken)
    {
        MicrosoftUpdateCatalogUpdate? update = await SearchByReleaseAsync(
                searchTarget.NormalizedHardwareId,
                releaseSearchOrder,
                cancellationToken)
            .ConfigureAwait(false);

        if (update is null)
        {
            update = await SearchByRawHardwareIdAsync(searchTarget.RawFallbackTerms, cancellationToken).ConfigureAwait(false);
        }

        if (update is null)
        {
            _logger.LogDebug("No Microsoft Update Catalog match found for device '{DeviceName}'.", searchTarget.DeviceName);
            return null;
        }

        IReadOnlyList<MicrosoftUpdateCatalogDownload> downloads = await _catalogClient
            .GetDownloadsAsync(update.UpdateId, cancellationToken)
            .ConfigureAwait(false);

        MicrosoftUpdateCatalogDownload? selectedDownload = MicrosoftUpdateCatalogSupport.SelectPreferredCab(downloads, targetArchitecture);
        if (selectedDownload is null)
        {
            _logger.LogInformation(
                "Microsoft Update Catalog update '{Title}' ({UpdateId}) has no CAB payload compatible with architecture '{Architecture}'.",
                update.Title,
                update.UpdateId,
                MicrosoftUpdateCatalogSupport.NormalizeArchitecture(targetArchitecture));
            return null;
        }

        return new CatalogDownloadCandidate
        {
            Update = update,
            Download = selectedDownload
        };
    }

    private async Task<ArtifactDownloadResult> DownloadPayloadAsync(
        CatalogDownloadCandidate candidate,
        string temporaryDirectory,
        Func<long, string, string> resolveCacheDirectory,
        CancellationToken cancellationToken)
    {
        string expectedHash = MicrosoftUpdateCatalogSupport.ResolvePreferredHash(candidate.Download);
        string relativePath = Path.Combine(
            MicrosoftUpdateCatalogSupport.SanitizePathSegment(candidate.Update.UpdateId),
            ResolveFileName(candidate.Download));
        string downloadDirectory = string.IsNullOrWhiteSpace(expectedHash)
            ? temporaryDirectory
            : resolveCacheDirectory(candidate.Update.SizeInBytes, relativePath);
        string destinationPath = Path.Combine(downloadDirectory, relativePath);

        ArtifactDownloadResult result = await _artifactDownloadService
            .DownloadAsync(
                candidate.Download.DownloadUrl,
                destinationPath,
                expectedHash: expectedHash,
                artifactKind: "MicrosoftUpdateCatalogDriver",
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Microsoft Update Catalog driver payload {Disposition}: {PayloadPath}",
            result.Downloaded ? "downloaded" : "reused",
            result.DestinationPath);
        return result;
    }

    private async Task<MicrosoftUpdateCatalogUpdate?> SearchByReleaseAsync(
        string normalizedHardwareId,
        IReadOnlyList<string> releaseSearchOrder,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(normalizedHardwareId))
        {
            return null;
        }

        foreach (string releaseId in releaseSearchOrder)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string query = MicrosoftUpdateCatalogSupport.BuildSearchQuery(releaseId, normalizedHardwareId);
            IReadOnlyList<MicrosoftUpdateCatalogUpdate> results = await _catalogClient
                .SearchAsync(query, descending: true, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            MicrosoftUpdateCatalogUpdate? update = results.FirstOrDefault();
            if (update is not null)
            {
                _logger.LogInformation(
                    "Found Microsoft Update Catalog match. ReleaseId={ReleaseId}, HardwareId={HardwareId}, UpdateId={UpdateId}, Title={Title}",
                    releaseId,
                    normalizedHardwareId,
                    update.UpdateId,
                    update.Title);
                return update;
            }
        }

        return null;
    }

    private async Task<MicrosoftUpdateCatalogUpdate?> SearchByRawHardwareIdAsync(
        IReadOnlyList<string> rawFallbackTerms,
        CancellationToken cancellationToken)
    {
        foreach (string rawHardwareId in rawFallbackTerms)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IReadOnlyList<MicrosoftUpdateCatalogUpdate> results = await _catalogClient
                .SearchAsync(rawHardwareId, descending: true, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            MicrosoftUpdateCatalogUpdate? update = results.FirstOrDefault();
            if (update is not null)
            {
                _logger.LogInformation(
                    "Found Microsoft Update Catalog fallback match. RawHardwareId={RawHardwareId}, UpdateId={UpdateId}, Title={Title}",
                    rawHardwareId,
                    update.UpdateId,
                    update.Title);
                return update;
            }
        }

        return null;
    }

    private static DriverSearchTarget[] BuildSearchTargets(HardwareProfile hardwareProfile)
    {
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var targets = new List<DriverSearchTarget>();

        foreach (PnpDeviceInfo device in hardwareProfile.PnpDevices)
        {
            if (device.ClassGuid.Equals(FirmwareClassGuid, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!IsCriticalCatalogDevice(device))
            {
                continue;
            }

            string normalizedHardwareId = MicrosoftUpdateCatalogSupport.TryExtractDriverSearchHardwareId(device) ?? string.Empty;
            string[] rawFallbackTerms = device.HardwareIds
                .Prepend(device.DeviceId)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(value => !value.Equals(normalizedHardwareId, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            string dedupeKey = !string.IsNullOrWhiteSpace(normalizedHardwareId)
                ? normalizedHardwareId
                : rawFallbackTerms.FirstOrDefault() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(dedupeKey) || !seenKeys.Add(dedupeKey))
            {
                continue;
            }

            targets.Add(new DriverSearchTarget
            {
                DeviceName = ResolveDeviceName(device),
                NormalizedHardwareId = normalizedHardwareId,
                RawFallbackTerms = rawFallbackTerms
            });
        }

        return targets.ToArray();
    }

    private static bool IsCriticalCatalogDevice(PnpDeviceInfo device)
    {
        string normalizedPnpClass = device.PnpClass.Trim();
        return CriticalPnpClasses.Contains(normalizedPnpClass, StringComparer.OrdinalIgnoreCase);
    }

    private static string ResolveDeviceName(PnpDeviceInfo device)
    {
        if (!string.IsNullOrWhiteSpace(device.Name))
        {
            return device.Name.Trim();
        }

        return !string.IsNullOrWhiteSpace(device.DeviceId)
            ? device.DeviceId.Trim()
            : "Unknown device";
    }

    private static string ResolveFileName(MicrosoftUpdateCatalogDownload download)
    {
        return string.IsNullOrWhiteSpace(download.FileName)
            ? MicrosoftUpdateCatalogSupport.ResolveFileNameFromUrl(download.DownloadUrl)
            : MicrosoftUpdateCatalogSupport.SanitizePathSegment(download.FileName);
    }

    private sealed record DriverSearchTarget
    {
        public required string DeviceName { get; init; }
        public required string NormalizedHardwareId { get; init; }
        public required IReadOnlyList<string> RawFallbackTerms { get; init; }
    }

    private sealed record CatalogDownloadCandidate
    {
        public required MicrosoftUpdateCatalogUpdate Update { get; init; }
        public required MicrosoftUpdateCatalogDownload Download { get; init; }
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
