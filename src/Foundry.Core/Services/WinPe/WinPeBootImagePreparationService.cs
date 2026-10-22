// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Reflection.PortableExecutable;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Xml.Linq;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Images;
using Foundry.Core.Services.Storage;
using Foundry.Utilities.IO;
using Foundry.Utilities.Imaging;
using Foundry.Utilities.Networking;
using Foundry.Utilities.Progress;
using Serilog;

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Replaces the boot image with a verified WinRE taken from the cache or from the newest Windows source package, and
/// stages the System32 dependencies the media needs; x64 falls back to the ADK Windows PE image when no WinRE is available.
/// </summary>
public sealed class WinPeBootImagePreparationService : IWinPeBootImagePreparationService
{
    /// <summary>
    /// Identifies the shared operating system catalog used to select Windows source packages.
    /// </summary>
    public static readonly Uri DefaultOperatingSystemCatalogUri =
        new("https://raw.githubusercontent.com/foundry-osd/catalog/refs/heads/main/Cache/OS/OperatingSystem.xml");

    private static readonly WinPeDownloadProgress WindowsSourceDownload = new()
    {
        Status = WinPeProgressStatuses.DownloadingWindowsSourcePackage
    };

    private const string WinReFileName = "winre.wim";

    private static readonly int[] SupportedWinReBuildMajors = [26100, 26200, 26300];

    private static readonly string[] RequiredWirelessDependencyFiles =
    [
        "dmcmnutils.dll",
        "mdmregistration.dll"
    ];

    private static readonly string[] RequiredArm64GraphicsDependencyFiles =
    [
        "d3dcompiler_47.dll",
        "d3d9.dll",
        "dwrite.dll",
        "winmm.dll"
    ];

    private readonly IWinPeProcessRunner _processRunner;
    private readonly HttpClient _httpClient;
    private readonly WinPeWorkspaceCleanupService _workspaceCleanup;
    private readonly Func<string, CancellationToken, Task<IReadOnlyList<WindowsImageMetadata>>> _readImageInfo;

    public WinPeBootImagePreparationService()
        : this(new WinPeProcessRunner(), new HttpClient())
    {
    }

    /// <summary>Uses the shared native DISM reader unless an isolated metadata boundary is supplied.</summary>
    internal WinPeBootImagePreparationService(IWinPeProcessRunner processRunner, HttpClient httpClient,
        WinPeWorkspaceCleanupService? workspaceCleanup = null,
        Func<string, CancellationToken, Task<IReadOnlyList<WindowsImageMetadata>>>? readImageInfo = null)
    {
        _processRunner = processRunner;
        _httpClient = httpClient;
        _workspaceCleanup = workspaceCleanup ?? new WinPeWorkspaceCleanupService();
        _readImageInfo = readImageInfo ?? NativeCustomImageMetadataReader.ReadNativeAsync;
    }

    /// <inheritdoc />
    public async Task<WinPeResult<WinPeBootImagePreparationResult>> PrepareAsync(
        WinPeBootImagePreparationOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        WinPeDiagnostic? validationError = ValidateOptions(options);
        if (validationError is not null)
        {
            return WinPeResult<WinPeBootImagePreparationResult>.Failure(validationError);
        }

        WinPeArchitecture architecture = options.Artifact.Architecture;
        var cache = new WinReCache(options.WinReCacheDirectoryPath);
        await using IAsyncDisposable cacheLock = await cache.AcquireLockAsync(architecture, options.WinPeLanguage, cancellationToken).ConfigureAwait(false);

        ReportProgress(options.Progress, 2, WinPeProgressStatuses.ResolvingWindowsSourceCatalog);
        WinPeResult<IReadOnlyList<WindowsSourceCandidate>> candidatesResult = await SelectCatalogCandidatesAsync(
            options.CatalogUri,
            architecture,
            options.WinPeLanguage,
            cancellationToken).ConfigureAwait(false);

        WinPeDiagnostic cause;
        if (candidatesResult.IsSuccess)
        {
            ReportProgress(options.Progress, 4, WinPeProgressStatuses.SelectedWindowsSourcePackage);
            foreach (WindowsSourceCandidate candidate in candidatesResult.Value!)
            {
                if (string.IsNullOrWhiteSpace(candidate.Source.Sha256)) continue;
                WinReCacheEntry? current = await cache.FindValidAsync(architecture, options.WinPeLanguage, candidate.Source.Sha256, cancellationToken).ConfigureAwait(false);
                if (current is not null)
                {
                    return UseCachedWinRe(options, current, cacheHit: true);
                }
            }

            var failures = new List<WinPeDiagnostic>();
            foreach (WindowsSourceCandidate candidate in candidatesResult.Value!)
            {
                WinPeResult<WinReCacheEntry> result = await TryExtractWinReAsync(options, candidate, cache, cancellationToken).ConfigureAwait(false);
                if (result.IsSuccess)
                {
                    DeleteSourceDownloads(options);
                    return UseCachedWinRe(options, result.Value!, cacheHit: false);
                }

                // Preserve a completed servicing failure when cancellation or an unsafe mount state forbids any fallback.
                if (cancellationToken.IsCancellationRequested ||
                    result.Error?.MountCleanupStatus is WinPeMountCleanupStatus.Failed or WinPeMountCleanupStatus.ExitUnconfirmed ||
                    result.Error?.Code == WinPeErrorCodes.WimUnmountFailed && result.Error?.MountCleanupStatus != WinPeMountCleanupStatus.Completed)
                {
                    return WinPeResult<WinPeBootImagePreparationResult>.Failure(result.Error!);
                }

                failures.Add(result.Error!);
            }

            cause = failures[^1] with
            {
                Message = "Failed to extract WinRE from every matching operating system source.",
                Details = string.Join(Environment.NewLine + Environment.NewLine,
                    failures.Select(failure => string.Join(Environment.NewLine, failure.Message, failure.Details)))
            };
        }
        else
        {
            cause = candidatesResult.Error!;
        }

        cancellationToken.ThrowIfCancellationRequested();
        WinReCacheEntry? previous = await cache.FindValidAsync(architecture, options.WinPeLanguage, null, cancellationToken).ConfigureAwait(false);
        if (previous is not null)
        {
            Log.ForContext<WinPeBootImagePreparationService>().Warning(
                "Using a previously cached WinRE because the newest one could not be obtained. Architecture={Architecture}, Language={Language}, Build={BuildMajor}.{BuildUbr}, FailureCode={FailureCode}, FailureMessage={FailureMessage}, FailureDetails={FailureDetails}",
                architecture, options.WinPeLanguage, previous.Manifest.BuildMajor, previous.Manifest.BuildUbr, cause.Code, cause.Message, cause.Details);
            return UseCachedWinRe(options, previous, cacheHit: true);
        }

        if (architecture == WinPeArchitecture.X64)
        {
            Log.ForContext<WinPeBootImagePreparationService>().Warning(
                "No WinRE could be obtained; the boot image falls back to the ADK Windows PE image without Wi-Fi support. Architecture={Architecture}, Language={Language}, FailureCode={FailureCode}, FailureMessage={FailureMessage}, FailureDetails={FailureDetails}",
                architecture, options.WinPeLanguage, cause.Code, cause.Message, cause.Details);
            return WinPeResult<WinPeBootImagePreparationResult>.Success(new WinPeBootImagePreparationResult
            {
                BootImageSource = WinPeBootImageSource.WinPe,
                FallbackCause = cause,
                DependencyFiles = []
            });
        }

        return WinPeResult<WinPeBootImagePreparationResult>.Failure(cause with
        {
            Message = "ARM64 boot media requires the Windows Recovery Environment, but no verified WinRE could be obtained from the cache or a Windows source package.",
            Details = string.Join(Environment.NewLine, cause.Message, cause.Details).Trim()
        });
    }

    /// <summary>
    /// Replaces boot.wim with the cached WinRE and stages the dependencies this media needs outside the cache.
    /// </summary>
    private static WinPeResult<WinPeBootImagePreparationResult> UseCachedWinRe(
        WinPeBootImagePreparationOptions options,
        WinReCacheEntry entry,
        bool cacheHit)
    {
        try
        {
            ReportProgress(options.Progress, 29, WinPeProgressStatuses.ReplacingBootImageWithWinRe);
            Directory.CreateDirectory(Path.GetDirectoryName(options.Artifact.BootWimPath)!);
            File.Copy(entry.GetFilePath(WinReFileName), options.Artifact.BootWimPath, overwrite: true);

            string dependencyDirectory = Path.Combine(options.Artifact.WorkingDirectoryPath, "boot-dependencies");
            Directory.CreateDirectory(dependencyDirectory);
            var dependencyFiles = new List<WinPeDependencyFile>();
            IEnumerable<string> wirelessFiles = options.IncludeWirelessSupport ? RequiredWirelessDependencyFiles : [];
            IEnumerable<string> graphicsFiles = options.Artifact.Architecture == WinPeArchitecture.Arm64 ? RequiredArm64GraphicsDependencyFiles : [];
            foreach ((string fileName, bool overwrite) in wirelessFiles.Select(name => (name, true)).Concat(graphicsFiles.Select(name => (name, false))))
            {
                string stagedPath = Path.Combine(dependencyDirectory, fileName);
                File.Copy(entry.GetFilePath(fileName), stagedPath, overwrite: true);
                dependencyFiles.Add(new WinPeDependencyFile { FileName = fileName, StagedPath = stagedPath, OverwriteExisting = overwrite });
            }

            Log.ForContext<WinPeBootImagePreparationService>().Information(
                "Boot image prepared from WinRE. Architecture={Architecture}, Language={Language}, Build={BuildMajor}.{BuildUbr}, Edition={Edition}, CacheHit={CacheHit}, IncludeWirelessSupport={IncludeWirelessSupport}",
                entry.Manifest.Architecture, entry.Manifest.Language, entry.Manifest.BuildMajor, entry.Manifest.BuildUbr,
                entry.Manifest.Edition, cacheHit, options.IncludeWirelessSupport);
            ReportProgress(options.Progress, 30, WinPeProgressStatuses.BootImageDependenciesReady);
            return WinPeResult<WinPeBootImagePreparationResult>.Success(new WinPeBootImagePreparationResult
            {
                BootImageSource = WinPeBootImageSource.WinRe,
                DependencyFiles = dependencyFiles
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return WinPeResult<WinPeBootImagePreparationResult>.Failure(new WinPeDiagnostic(
                WinPeErrorCodes.WinReExtractionFailed,
                "Failed to copy the cached WinRE into the boot media workspace.",
                ex.Message,
                exception: ex));
        }
    }

    /// <summary>
    /// Removes Windows source downloads once their WinRE is cached, including packages of older Foundry versions;
    /// a package leased by another Foundry process stays until a later build.
    /// </summary>
    private static void DeleteSourceDownloads(WinPeBootImagePreparationOptions options)
    {
        new AuthoringArtifactCache(options.CacheDirectoryPath).PurgeUnleasedEntries();
        if (options.LegacySourceDirectoryPath is not null && Directory.Exists(options.LegacySourceDirectoryPath))
        {
            foreach (string directory in Directory.EnumerateDirectories(options.LegacySourceDirectoryPath)) TryDeleteDirectory(directory);
            foreach (string file in Directory.EnumerateFiles(options.LegacySourceDirectoryPath)) TryDeleteFile(file);
        }
    }

    /// <summary>
    /// Selects Pro and Enterprise fallback sources matching the boot language and architecture, newest build and UBR first.
    /// </summary>
    /// <remarks>
    /// 24H2, 25H2 and 26H2 share the 26100 servicing branch, so their WinRE accepts the ADK 26100 packages; the newest
    /// one carries the newest Safe OS servicing and therefore the newest boot drivers.
    /// </remarks>
    internal static WinPeResult<IReadOnlyList<WindowsSourceCandidate>> SelectCatalogCandidates(
        string catalogXml,
        WinPeArchitecture architecture,
        string languageCode)
    {
        if (string.IsNullOrWhiteSpace(catalogXml))
        {
            return WinPeResult<IReadOnlyList<WindowsSourceCandidate>>.Failure(
                WinPeErrorCodes.OperatingSystemCatalogParseFailed,
                "The operating system catalog is empty.");
        }

        try
        {
            string normalizedArchitecture = NormalizeArchitecture(architecture);
            string normalizedLanguage = WinPeLanguageUtility.Normalize(languageCode);
            XDocument document = XDocument.Parse(catalogXml);

            List<WindowsSourceCatalogItem> matchingItems = document.Descendants("Item")
                .Select(ParseCatalogItem)
                .Where(item =>
                    item.WindowsRelease.Equals("11", StringComparison.OrdinalIgnoreCase) &&
                    SupportedWinReBuildMajors.Contains(item.BuildMajor) &&
                    item.Architecture.Equals(normalizedArchitecture, StringComparison.OrdinalIgnoreCase) &&
                    WinPeLanguageUtility.Normalize(item.LanguageCode).Equals(normalizedLanguage, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(item.Url))
                .GroupBy(item => item.Url, StringComparer.OrdinalIgnoreCase)
                .Select(group => group
                    .OrderByDescending(item => item.BuildMajor)
                    .ThenByDescending(item => item.BuildUbr)
                    .ThenBy(item => GetLicenseChannelOrder(item.LicenseChannel))
                    .ThenBy(item => GetClientTypeOrder(item.ClientType))
                    .First())
                .ToList();

            WindowsSourceCatalogItem? proSource = SelectPreferredSourceItem(matchingItems, "CLIENTCONSUMER");
            WindowsSourceCatalogItem? enterpriseSource = SelectPreferredSourceItem(matchingItems, "CLIENTBUSINESS");

            var candidates = new List<WindowsSourceCandidate>(2);
            if (proSource is not null)
            {
                candidates.Add(new WindowsSourceCandidate
                {
                    RequestedEdition = "Pro",
                    Source = proSource
                });
            }

            if (enterpriseSource is not null)
            {
                candidates.Add(new WindowsSourceCandidate
                {
                    RequestedEdition = "Enterprise",
                    Source = enterpriseSource
                });
            }

            if (candidates.Count == 0)
            {
                return WinPeResult<IReadOnlyList<WindowsSourceCandidate>>.Failure(
                    WinPeErrorCodes.WinReSourceSelectionFailed,
                    "No Windows 11 source of the 26100 servicing branch (24H2, 25H2 or 26H2) matched the requested architecture and language.",
                    $"Architecture={normalizedArchitecture}, Language={normalizedLanguage}");
            }

            return WinPeResult<IReadOnlyList<WindowsSourceCandidate>>.Success(candidates);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            return WinPeResult<IReadOnlyList<WindowsSourceCandidate>>.Failure(new WinPeDiagnostic(
                WinPeErrorCodes.OperatingSystemCatalogParseFailed,
                "Failed to parse the operating system catalog.",
                ex.Message,
                exception: ex));
        }
    }

    /// <summary>Resolves one exact canonical edition from native metadata, independently of localized image names.</summary>
    internal static WinPeResult<int> ResolveImageIndexFromMetadata(IReadOnlyList<WindowsImageMetadata> images, string requestedEdition)
    {
        WindowsEditionDefinition? definition = WindowsEditionCatalog.Find(requestedEdition);
        if (definition is null)
        {
            return WinPeResult<int>.Failure(
                WinPeErrorCodes.ValidationFailed,
                "A supported Windows edition is required.");
        }

        WindowsImageMetadata[] matches = images
            .Where(image => image.EditionId.Equals(definition.EditionId, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (matches.Length != 1 || matches[0].Index < 1)
        {
            return WinPeResult<int>.Failure(
                WinPeErrorCodes.WinReIndexResolutionFailed,
                $"Could not resolve a unique Windows image index for edition '{requestedEdition}'.",
                $"RequestedEditionId={definition.EditionId}; MatchCount={matches.Length}; AvailableEditionIds={string.Join(", ", images.Select(image => $"{image.Index}: {image.EditionId}"))}");
        }

        return WinPeResult<int>.Success(matches[0].Index);
    }

    /// <summary>
    /// Copies the System32 dependencies cached with WinRE before the source mount is discarded: the wireless files for
    /// every architecture, so one entry serves media with and without Wi-Fi, and the ARM64 graphics files, which must be ARM64 images.
    /// </summary>
    internal static WinPeResult PrepareDependencyFiles(
        string mountedImagePath,
        string destinationDirectoryPath,
        WinPeArchitecture architecture)
    {
        string sourceSystem32Path = Path.Combine(mountedImagePath, "Windows", "System32");

        try
        {
            Directory.CreateDirectory(destinationDirectoryPath);
            foreach (string fileName in GetDependencyFileNames(architecture))
            {
                string sourcePath = Path.Combine(sourceSystem32Path, fileName);
                if (!File.Exists(sourcePath))
                {
                    return WinPeResult.Failure(
                        WinPeErrorCodes.WinReExtractionFailed,
                        $"The selected operating system image is missing the required boot dependency '{fileName}'.",
                        $"Expected path: '{sourcePath}'.");
                }

                bool isGraphicsDependency = RequiredArm64GraphicsDependencyFiles.Contains(fileName, StringComparer.OrdinalIgnoreCase);
                if (isGraphicsDependency && !IsArm64Image(sourcePath))
                {
                    return WinPeResult.Failure(
                        WinPeErrorCodes.WinReExtractionFailed,
                        $"The required graphics dependency '{fileName}' is not a valid ARM64 image.",
                        $"Source path: '{sourcePath}'.");
                }

                File.Copy(sourcePath, Path.Combine(destinationDirectoryPath, fileName), overwrite: true);
            }

            return WinPeResult.Success();
        }
        catch (Exception ex)
        {
            return WinPeResult.Failure(new WinPeDiagnostic(
                WinPeErrorCodes.WinReExtractionFailed,
                "Failed to stage required boot dependency files from the mounted operating system image.",
                ex.Message,
                exception: ex));
        }
    }

    private static IEnumerable<string> GetDependencyFileNames(WinPeArchitecture architecture) =>
        architecture == WinPeArchitecture.Arm64
            ? RequiredWirelessDependencyFiles.Concat(RequiredArm64GraphicsDependencyFiles)
            : RequiredWirelessDependencyFiles;

    private static bool IsArm64Image(string path)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            using var reader = new PEReader(stream);
            return reader.PEHeaders.PEHeader is not null && reader.PEHeaders.CoffHeader.Machine == Machine.Arm64;
        }
        catch (BadImageFormatException)
        {
            return false;
        }
    }

    private async Task<WinPeResult<IReadOnlyList<WindowsSourceCandidate>>> SelectCatalogCandidatesAsync(
        Uri catalogUri,
        WinPeArchitecture architecture,
        string languageCode,
        CancellationToken cancellationToken)
    {
        try
        {
            string catalogXml = await _httpClient.GetStringAsync(catalogUri, cancellationToken).ConfigureAwait(false);
            return SelectCatalogCandidates(catalogXml, architecture, languageCode);
        }
        catch (Exception ex) when (
            ex is HttpRequestException or InvalidOperationException ||
            ex is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return WinPeResult<IReadOnlyList<WindowsSourceCandidate>>.Failure(new WinPeDiagnostic(
                WinPeErrorCodes.OperatingSystemCatalogFetchFailed,
                "Failed to download the operating system catalog.",
                ex.Message,
                failureKind: WinPeFailureKinds.Network,
                failureReason: ex is TaskCanceledException ? WinPeFailureReasons.Timeout : null,
                exception: ex));
        }
    }

    /// <summary>
    /// Downloads one source package, copies its WinRE and the dependencies of this architecture into a pending cache
    /// folder, discards the source mount and publishes the cache entry.
    /// </summary>
    private async Task<WinPeResult<WinReCacheEntry>> TryExtractWinReAsync(
        WinPeBootImagePreparationOptions options,
        WindowsSourceCandidate candidate,
        WinReCache cache,
        CancellationToken cancellationToken)
    {
        string candidateName = PathSegment.Sanitize(candidate.RequestedEdition);
        string sourceDirectory = Path.Combine(options.Artifact.WorkingDirectoryPath, $"windows-source-{candidateName}");
        string exportDirectory = Path.Combine(sourceDirectory, "export");
        string mountDirectory = Path.Combine(sourceDirectory, "install-mount");
        string installWimPath = Path.Combine(exportDirectory, "install.wim");

        WinPeMountSession? session = null;
        string? pendingDirectory = null;
        WinPeResult<WinReCacheEntry> result;
        ExceptionDispatchInfo? cancellationFailure = null;
        try
        {
            result = await PrepareCandidateAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (session is not null)
        {
            cancellationFailure = ExceptionDispatchInfo.Capture(exception);
            result = WinPeResult<WinReCacheEntry>.Failure(new WinPeDiagnostic(
                WinPeErrorCodes.OperationCancelled,
                "Boot image preparation was cancelled.",
                exception.Message,
                exception: exception));
        }
        finally
        {
            if (session is not null)
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }

            if (pendingDirectory is not null)
            {
                TryDeleteDirectory(pendingDirectory);
            }
        }

        // Dispose can retry a completed unmount failure. Settle that final attempt before
        // deciding whether another candidate can safely start servicing.
        if (session?.CleanupStatus == WinPeMountCleanupStatus.Completed)
        {
            cancellationFailure?.Throw();
        }

        if (session is null || result.IsSuccess)
        {
            return result;
        }

        WinPeDiagnostic diagnostic = result.Error!;
        if (session.CleanupStatus is WinPeMountCleanupStatus.Failed or WinPeMountCleanupStatus.ExitUnconfirmed)
        {
            WinPeDiagnostic cleanupFailure = session.CleanupFailure!;
            diagnostic = cleanupFailure with
            {
                Details = string.Join(Environment.NewLine,
                    "Preparation diagnostics:", diagnostic.Message, diagnostic.Details,
                    "Final cleanup diagnostics:", cleanupFailure.Message, cleanupFailure.Details)
            };
        }

        return WinPeResult<WinReCacheEntry>.Failure(diagnostic with
        {
            MountCleanupStatus = session.CleanupStatus
        });

        async Task<WinPeResult<WinReCacheEntry>> PrepareCandidateAsync()
        {
            try
            {
                WinPeResult cleanup = _workspaceCleanup.Delete(sourceDirectory);
                if (!cleanup.IsSuccess) return WinPeResult<WinReCacheEntry>.Failure(cleanup.Error!);
                Directory.CreateDirectory(sourceDirectory);
                Directory.CreateDirectory(exportDirectory);
                ReportProgress(options.Progress, 5, WinPeProgressStatuses.PreparingWindowsSourcePackage);

                WinPeResult<CachedArtifactLease> sourcePathResult = await EnsureDownloadedAsync(
                    options.CacheDirectoryPath,
                    candidate.Source,
                    options.DownloadProgress,
                    cancellationToken).ConfigureAwait(false);

                if (!sourcePathResult.IsSuccess)
                {
                    return WinPeResult<WinReCacheEntry>.Failure(sourcePathResult.Error!);
                }

                await using CachedArtifactLease sourceLease = sourcePathResult.Value!;
                ReportProgress(options.Progress, 16, WinPeProgressStatuses.ResolvingWindowsImageIndex);
                WinPeResult<int> indexResult = await ResolveImageIndexAsync(
                    sourceLease.Path,
                    candidate.RequestedEdition,
                    cancellationToken).ConfigureAwait(false);

                if (!indexResult.IsSuccess)
                {
                    return WinPeResult<WinReCacheEntry>.Failure(indexResult.Error!);
                }

                ReportProgress(options.Progress, 19, WinPeProgressStatuses.ExportingWindowsSourceImage);
                cancellationToken.ThrowIfCancellationRequested();
                WinPeProcessExecution exportResult = await WinPeDismProcessRunner.RunAsync(
                    _processRunner,
                    options.Tools.DismPath,
                    $"/Export-Image /SourceImageFile:{WinPeProcessRunner.Quote(sourceLease.Path)} /SourceIndex:{indexResult.Value} /DestinationImageFile:{WinPeProcessRunner.Quote(installWimPath)} /Compress:max /CheckIntegrity",
                    options.Artifact.WorkingDirectoryPath,
                    WinPeProgressStatuses.DismExportingWindowsImage,
                    CreateDismProgress(options.Progress, 19, WinPeProgressStatuses.ExportingWindowsSourceImage),
                    CancellationToken.None).ConfigureAwait(false);

                if (!exportResult.IsSuccess)
                {
                    return WinPeResult<WinReCacheEntry>.Failure(exportResult.ToFailureDiagnostic(
                        WinPeErrorCodes.WinReExtractionFailed,
                        $"Failed to export the {candidate.RequestedEdition} image from the Windows source package.",
                        stage: "Export Windows source image",
                        toolName: "dism.exe"));
                }
                cancellationToken.ThrowIfCancellationRequested();

                if (!File.Exists(installWimPath))
                {
                    return WinPeResult<WinReCacheEntry>.Failure(new WinPeDiagnostic(
                        WinPeErrorCodes.WinReExtractionFailed,
                        "DISM completed without producing the exported Windows image.",
                        exportResult.ToDiagnosticText(),
                        stage: "Export Windows source image",
                        exitCode: exportResult.ExitCode,
                        failureKind: WinPeFailureKinds.Process,
                        failureReason: WinPeFailureReasons.ArtifactMissing,
                        toolName: "dism.exe"));
                }

                ReportProgress(options.Progress, 24, WinPeProgressStatuses.MountingWindowsSourceImage);
                cancellationToken.ThrowIfCancellationRequested();
                WinPeResult<WinPeMountSession> mountResult = await WinPeMountSession.MountAsync(
                    _processRunner,
                    options.Tools.DismPath,
                    installWimPath,
                    mountDirectory,
                    options.Artifact.WorkingDirectoryPath,
                    CancellationToken.None,
                    CreateDismProgress(options.Progress, 24, WinPeProgressStatuses.MountingWindowsSourceImage)).ConfigureAwait(false);

                if (!mountResult.IsSuccess)
                {
                    return WinPeResult<WinReCacheEntry>.Failure(mountResult.Error!);
                }

                session = mountResult.Value!;
                cancellationToken.ThrowIfCancellationRequested();
                string winRePath = Path.Combine(mountDirectory, "Windows", "System32", "Recovery", WinReFileName);
                if (!File.Exists(winRePath))
                {
                    return await FailWithDiscardAsync<WinReCacheEntry>(
                        new WinPeDiagnostic(
                            WinPeErrorCodes.WinReExtractionFailed,
                            "The selected operating system image does not contain winre.wim.",
                            $"Expected path: '{winRePath}'."),
                        session).ConfigureAwait(false);
                }

                ReportProgress(options.Progress, 27, WinPeProgressStatuses.StagingBootImageDependencies);
                pendingDirectory = cache.CreatePendingDirectory(options.Artifact.Architecture, options.WinPeLanguage);
                WinPeResult dependencyResult = PrepareDependencyFiles(mountDirectory, pendingDirectory, options.Artifact.Architecture);
                if (!dependencyResult.IsSuccess)
                {
                    return await FailWithDiscardAsync<WinReCacheEntry>(dependencyResult.Error!, session).ConfigureAwait(false);
                }

                File.Copy(winRePath, Path.Combine(pendingDirectory, WinReFileName));
                WinPeResult discardResult = await session.DiscardAsync().ConfigureAwait(false);
                if (!discardResult.IsSuccess)
                {
                    return WinPeResult<WinReCacheEntry>.Failure(discardResult.Error!);
                }
                cancellationToken.ThrowIfCancellationRequested();

                WinReCacheEntry entry = await cache.PublishAsync(
                    pendingDirectory,
                    new WinReCacheSource(
                        options.Artifact.Architecture,
                        options.WinPeLanguage,
                        candidate.Source.BuildMajor,
                        candidate.Source.BuildUbr,
                        candidate.RequestedEdition,
                        candidate.Source.FileName,
                        candidate.Source.Sha256),
                    [WinReFileName, .. GetDependencyFileNames(options.Artifact.Architecture)],
                    cancellationToken).ConfigureAwait(false);
                pendingDirectory = null;

                TryDeleteDirectory(sourceDirectory);
                return WinPeResult<WinReCacheEntry>.Success(entry);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (session is not null)
                {
                    return await FailWithDiscardAsync<WinReCacheEntry>(
                        new WinPeDiagnostic(
                            WinPeErrorCodes.WinReExtractionFailed,
                            "Failed to extract WinRE from the Windows source image.",
                            ex.Message,
                            exception: ex),
                        session).ConfigureAwait(false);
                }

                return WinPeResult<WinReCacheEntry>.Failure(new WinPeDiagnostic(
                    WinPeErrorCodes.WinReExtractionFailed,
                    "Failed to extract WinRE from the Windows source image.",
                    ex.Message,
                    exception: ex));
            }
        }
    }

    private async Task<WinPeResult<CachedArtifactLease>> EnsureDownloadedAsync(
        string cacheDirectoryPath,
        WindowsSourceCatalogItem source,
        IProgress<WinPeDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        string fileName = Path.GetFileName(BuildCachedSourcePath(cacheDirectoryPath, source));
        if (!Uri.TryCreate(WindowsUpdateContentUrl.Normalize(source.Url), UriKind.Absolute, out Uri? sourceUri))
            return WinPeResult<CachedArtifactLease>.Failure(WinPeErrorCodes.DownloadFailed,
                "The Windows source package URL is invalid.", source.Url);
        try
        {
            var request = new CachedArtifactRequest(AuthoringArtifactKind.WindowsSource, JsonSerializer.Serialize(source),
                fileName, source.Sha256, null, AllowCompletedTransferReuse: source.BuildMajor > 0 && !string.IsNullOrWhiteSpace(source.ReleaseId));
            CachedArtifactLease lease = await new AuthoringArtifactCache(cacheDirectoryPath).AcquireAsync(request, async (path, token) =>
            {
                WinPeDownloadReporting.Report(progress, WindowsSourceDownload, 0);
                await HttpTransfer.RunAsync(async (transferToken, reportProgress) =>
                {
                    using HttpResponseMessage response = await _httpClient.GetAsync(sourceUri, HttpCompletionOption.ResponseHeadersRead, transferToken).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();
                    long? length = response.Content.Headers.ContentLength;
                    await using Stream input = await response.Content.ReadAsStreamAsync(transferToken).ConfigureAwait(false);
                    await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                    await WinPeDownloadReporting.CopyAsync(input, output, length, WindowsSourceDownload, progress, reportProgress, transferToken).ConfigureAwait(false);
                    if (length.HasValue && output.Length != length.Value) throw new InvalidDataException("Windows source transfer is incomplete.");
                    return true;
                }, token).ConfigureAwait(false);
            }, cancellationToken, new CacheVerificationProgress(progress, fileName)).ConfigureAwait(false);
            WinPeDownloadReporting.Report(progress, new WinPeDownloadProgress
            {
                Status = lease.CacheHit
                    ? WinPeProgressStatuses.ReusingWindowsSourcePackage
                    : WinPeProgressStatuses.WindowsSourcePackageReady
            }, 100);
            return WinPeResult<CachedArtifactLease>.Success(lease);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or UnauthorizedAccessException or TimeoutException or ArgumentException)
        {
            return WinPeResult<CachedArtifactLease>.Failure(new WinPeDiagnostic(
                exception is InvalidDataException or ArgumentException ? WinPeErrorCodes.HashMismatch : WinPeErrorCodes.DownloadFailed,
                "Failed to acquire a verified Windows source package.", exception.Message, exception: exception));
        }
    }

    private async Task<WinPeResult<int>> ResolveImageIndexAsync(
        string sourceImagePath,
        string requestedEdition,
        CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<WindowsImageMetadata> images = await _readImageInfo(sourceImagePath, cancellationToken).ConfigureAwait(false);
            return ResolveImageIndexFromMetadata(images, requestedEdition);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return WinPeResult<int>.Failure(new WinPeDiagnostic(
                WinPeErrorCodes.WinReIndexResolutionFailed,
                "Failed to inspect the Windows source package image indexes with native DISM.",
                ex.Message,
                stage: "Inspect Windows source image",
                exception: ex));
        }
    }

    private static WinPeDiagnostic? ValidateOptions(WinPeBootImagePreparationOptions? options)
    {
        if (options is null)
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "Boot image preparation options are required.");
        }

        if (string.IsNullOrWhiteSpace(options.Artifact.WorkingDirectoryPath))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "WinPE working directory path is required.");
        }

        if (string.IsNullOrWhiteSpace(options.Artifact.BootWimPath))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "WinPE boot.wim path is required.");
        }

        if (string.IsNullOrWhiteSpace(options.Tools.DismPath))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "DISM path is required.");
        }

        if (string.IsNullOrWhiteSpace(options.CacheDirectoryPath))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "Windows source cache directory path is required.");
        }

        if (string.IsNullOrWhiteSpace(options.WinReCacheDirectoryPath))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "WinRE cache directory path is required.");
        }

        // Source downloads are purged after each extraction, so they must never contain or sit inside the WinRE cache.
        if (new[] { options.CacheDirectoryPath, options.LegacySourceDirectoryPath }.OfType<string>()
            .Any(path => IsSameOrNested(path, options.WinReCacheDirectoryPath)))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "The WinRE cache must be separate from the Windows source download directories.");
        }

        return null;
    }

    private static bool IsSameOrNested(string left, string right)
    {
        string a = Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)) + Path.DirectorySeparatorChar;
        string b = Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)) + Path.DirectorySeparatorChar;
        return a.StartsWith(b, StringComparison.OrdinalIgnoreCase) || b.StartsWith(a, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildCachedSourcePath(string cacheDirectoryPath, WindowsSourceCatalogItem source)
    {
        string fileName = string.IsNullOrWhiteSpace(source.FileName)
            ? $"{source.ReleaseId}-{source.Architecture}-{source.LanguageCode}.esd"
            : source.FileName;

        return Path.Combine(
            cacheDirectoryPath,
            PathSegment.Sanitize(fileName));
    }

    private static async Task<WinPeResult<T>> FailWithDiscardAsync<T>(
        WinPeDiagnostic primaryDiagnostic,
        WinPeMountSession session)
    {
        WinPeResult discardResult = await session.DiscardAsync().ConfigureAwait(false);
        if (discardResult.IsSuccess)
        {
            return WinPeResult<T>.Failure(primaryDiagnostic);
        }

        string details = string.Join(
            Environment.NewLine,
            primaryDiagnostic.Details ?? string.Empty,
            "Discard diagnostics:",
            discardResult.Error?.Details ?? string.Empty).Trim();

        return WinPeResult<T>.Failure(primaryDiagnostic with { Details = details });
    }

    private static WindowsSourceCatalogItem ParseCatalogItem(XElement item)
    {
        return new WindowsSourceCatalogItem
        {
            WindowsRelease = ReadElement(item, "WindowsRelease"),
            ReleaseId = ReadElement(item, "ReleaseId"),
            BuildMajor = ParseInt(ReadElement(item, "BuildMajor")),
            BuildUbr = ParseInt(ReadElement(item, "BuildUbr")),
            Architecture = NormalizeArchitecture(ReadElement(item, "Architecture")),
            LanguageCode = ReadElement(item, "LanguageCode"),
            ClientType = ReadElement(item, "ClientType"),
            LicenseChannel = ReadElement(item, "LicenseChannel"),
            FileName = ReadElement(item, "FileName"),
            Url = ReadElement(item, "Url"),
            Sha256 = ReadElement(item, "Sha256")
        };
    }

    private static void ReportProgress(
        IProgress<WinPeMountedImageCustomizationProgress>? progress,
        int percent,
        string status)
    {
        progress?.Report(new WinPeMountedImageCustomizationProgress
        {
            Percent = Math.Clamp(percent, 0, 100),
            Status = status
        });
    }

    private static IProgress<WinPeDismProgress>? CreateDismProgress(
        IProgress<WinPeMountedImageCustomizationProgress>? progress,
        int percent,
        string status)
    {
        return progress is null ? null : new WinPeDismProgressForwarder(progress, percent, status);
    }

    private static string ReadElement(XElement parent, string elementName)
    {
        return (parent.Elements()
            .FirstOrDefault(element => element.Name.LocalName.Equals(elementName, StringComparison.OrdinalIgnoreCase))
            ?.Value ?? string.Empty).Trim();
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    private static int ParseInt(string value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : 0;
    }

    private static string NormalizeArchitecture(string value)
    {
        string normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "amd64" => "x64",
            "aarch64" => "arm64",
            _ => normalized
        };
    }

    private static string NormalizeArchitecture(WinPeArchitecture architecture)
    {
        return architecture switch
        {
            WinPeArchitecture.X64 => "x64",
            WinPeArchitecture.Arm64 => "arm64",
            _ => architecture.ToString().ToLowerInvariant()
        };
    }

    private static WindowsSourceCatalogItem? SelectPreferredSourceItem(
        IEnumerable<WindowsSourceCatalogItem> items,
        string clientType)
    {
        return items
            .Where(item => item.ClientType.Equals(clientType, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.BuildMajor)
            .ThenByDescending(item => item.BuildUbr)
            .ThenBy(item => GetLicenseChannelOrder(item.LicenseChannel))
            .FirstOrDefault();
    }

    private static int GetLicenseChannelOrder(string licenseChannel)
    {
        return licenseChannel.Equals("RET", StringComparison.OrdinalIgnoreCase)
            ? 0
            : licenseChannel.Equals("VOL", StringComparison.OrdinalIgnoreCase)
                ? 1
                : 99;
    }

    private static int GetClientTypeOrder(string clientType)
    {
        return clientType.Equals("CLIENTCONSUMER", StringComparison.OrdinalIgnoreCase)
            ? 0
            : clientType.Equals("CLIENTBUSINESS", StringComparison.OrdinalIgnoreCase)
                ? 1
                : 99;
    }

}
