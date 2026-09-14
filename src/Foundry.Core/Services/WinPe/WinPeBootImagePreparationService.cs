// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Foundry.Utilities.IO;
using Foundry.Utilities.Networking;
using Foundry.Utilities.Progress;

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Stages boot dependencies from one Windows source mount and optionally replaces the boot image with WinRE.
/// </summary>
public sealed class WinPeBootImagePreparationService : IWinPeBootImagePreparationService
{
    /// <summary>
    /// Identifies the shared operating system catalog used to select Windows source packages.
    /// </summary>
    public static readonly Uri DefaultOperatingSystemCatalogUri =
        new("https://raw.githubusercontent.com/foundry-osd/catalog/refs/heads/main/Cache/OS/OperatingSystem.xml");

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

    public WinPeBootImagePreparationService()
        : this(new WinPeProcessRunner(), new HttpClient())
    {
    }

    internal WinPeBootImagePreparationService(IWinPeProcessRunner processRunner, HttpClient httpClient)
    {
        _processRunner = processRunner;
        _httpClient = httpClient;
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

        ReportProgress(options.Progress, 2, "Resolving Windows source catalog.");
        WinPeResult<IReadOnlyList<WindowsSourceCandidate>> candidatesResult = await SelectCatalogCandidatesAsync(
            options.CatalogUri,
            options.Artifact.Architecture,
            options.WinPeLanguage,
            cancellationToken).ConfigureAwait(false);

        if (!candidatesResult.IsSuccess)
        {
            return WinPeResult<WinPeBootImagePreparationResult>.Failure(candidatesResult.Error!);
        }

        ReportProgress(options.Progress, 4, "Selected Windows source package.");
        var failures = new List<WinPeDiagnostic>();
        foreach (WindowsSourceCandidate candidate in candidatesResult.Value!)
        {
            WinPeResult<WinPeBootImagePreparationResult> result = await TryPrepareFromSourceAsync(
                options,
                candidate,
                cancellationToken).ConfigureAwait(false);

            if (result.IsSuccess)
            {
                return result;
            }

            failures.Add(result.Error!);
        }

        return WinPeResult<WinPeBootImagePreparationResult>.Failure(failures[^1] with
        {
            Code = WinPeErrorCodes.WinReExtractionFailed,
            Message = "Failed to prepare boot image dependencies from every matching operating system source.",
            Details = string.Join(Environment.NewLine + Environment.NewLine,
                failures.Select(failure => string.Join(Environment.NewLine, failure.Message, failure.Details)))
        });
    }

    /// <summary>
    /// Selects Pro and Enterprise fallback sources matching the boot language, architecture, and supported 26100 build line.
    /// </summary>
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
                    item.ReleaseId.Equals("24H2", StringComparison.OrdinalIgnoreCase) &&
                    item.BuildMajor == 26100 &&
                    item.Architecture.Equals(normalizedArchitecture, StringComparison.OrdinalIgnoreCase) &&
                    WinPeLanguageUtility.Normalize(item.LanguageCode).Equals(normalizedLanguage, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(item.Url))
                .GroupBy(item => item.Url, StringComparer.OrdinalIgnoreCase)
                .Select(group => group
                    .OrderBy(item => GetLicenseChannelOrder(item.LicenseChannel))
                    .ThenBy(item => GetClientTypeOrder(item.ClientType))
                    .ThenByDescending(item => item.BuildMajor)
                    .ThenByDescending(item => item.BuildUbr)
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
                    "No Windows 11 24H2 Windows source matched the requested architecture and language.",
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

    internal static async Task<WinPeResult> ValidateHashIfRequestedAsync(
        string filePath,
        string? expectedHash,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(expectedHash))
        {
            return WinPeResult.Success();
        }

        string normalizedExpectedHash = expectedHash.Trim().Replace("-", string.Empty, StringComparison.OrdinalIgnoreCase);
        if (normalizedExpectedHash.Length != 64)
        {
            return WinPeResult.Success();
        }

        string actualHash = await FileHash.ComputeSha256Async(filePath, cancellationToken).ConfigureAwait(false);
        if (normalizedExpectedHash.Equals(actualHash, StringComparison.OrdinalIgnoreCase))
        {
            return WinPeResult.Success();
        }

        return WinPeResult.Failure(
            WinPeErrorCodes.HashMismatch,
            "The cached Windows source package failed hash validation.",
            $"Expected SHA256={normalizedExpectedHash}; Actual SHA256={actualHash}.");
    }

    internal static WinPeResult<int> ResolveImageIndexFromOutput(string output, string requestedEdition)
    {
        string normalizedRequestedEdition = NormalizeToken(requestedEdition);
        if (normalizedRequestedEdition.Length == 0)
        {
            return WinPeResult<int>.Failure(
                WinPeErrorCodes.ValidationFailed,
                "Requested Windows edition is required.");
        }

        ImageIndexDescriptor? match = ParseImageDescriptors(output)
            .FirstOrDefault(descriptor =>
                ContainsNormalized(descriptor.Name, normalizedRequestedEdition) ||
                ContainsNormalized(descriptor.Edition, normalizedRequestedEdition) ||
                ContainsNormalized(descriptor.EditionId, normalizedRequestedEdition));

        if (match is null)
        {
            return WinPeResult<int>.Failure(
                WinPeErrorCodes.WinReIndexResolutionFailed,
                $"Could not resolve a Windows image index for edition '{requestedEdition}'.",
                output);
        }

        return WinPeResult<int>.Success(match.Index);
    }

    /// <summary>
    /// Stages all required dependencies before source discard; graphics binaries must be ARM64 and preserve existing boot files.
    /// </summary>
    internal static WinPeResult<WinPeBootImagePreparationResult> PrepareDependencyFiles(
        string mountedImagePath,
        string dependencyDirectoryPath,
        WinPeArchitecture architecture,
        WinPeBootImageSource bootImageSource)
    {
        string sourceSystem32Path = Path.Combine(mountedImagePath, "Windows", "System32");

        try
        {
            Directory.CreateDirectory(dependencyDirectoryPath);

            IEnumerable<string> requiredFiles = bootImageSource == WinPeBootImageSource.WinReWifi
                ? RequiredWirelessDependencyFiles
                : [];
            if (architecture == WinPeArchitecture.Arm64)
            {
                requiredFiles = requiredFiles.Concat(RequiredArm64GraphicsDependencyFiles);
            }

            var dependencyFiles = new List<WinPeDependencyFile>();
            foreach (string fileName in requiredFiles)
            {
                string sourcePath = Path.Combine(sourceSystem32Path, fileName);
                if (!File.Exists(sourcePath))
                {
                    return WinPeResult<WinPeBootImagePreparationResult>.Failure(
                        WinPeErrorCodes.WinReExtractionFailed,
                        $"The selected operating system image is missing the required boot dependency '{fileName}'.",
                        $"Expected path: '{sourcePath}'.");
                }

                bool isGraphicsDependency = RequiredArm64GraphicsDependencyFiles.Contains(fileName, StringComparer.OrdinalIgnoreCase);
                if (isGraphicsDependency && !IsArm64Image(sourcePath))
                {
                    return WinPeResult<WinPeBootImagePreparationResult>.Failure(
                        WinPeErrorCodes.WinReExtractionFailed,
                        $"The required graphics dependency '{fileName}' is not a valid ARM64 image.",
                        $"Source path: '{sourcePath}'.");
                }

                string stagedPath = Path.Combine(dependencyDirectoryPath, fileName);
                File.Copy(sourcePath, stagedPath, overwrite: true);
                dependencyFiles.Add(new WinPeDependencyFile
                {
                    FileName = fileName,
                    StagedPath = stagedPath,
                    OverwriteExisting = !isGraphicsDependency
                });
            }

            return WinPeResult<WinPeBootImagePreparationResult>.Success(new WinPeBootImagePreparationResult
            {
                DependencyFiles = dependencyFiles
            });
        }
        catch (Exception ex)
        {
            return WinPeResult<WinPeBootImagePreparationResult>.Failure(new WinPeDiagnostic(
                WinPeErrorCodes.WinReExtractionFailed,
                "Failed to stage required boot dependency files from the mounted operating system image.",
                ex.Message,
                exception: ex));
        }
    }

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
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return WinPeResult<IReadOnlyList<WindowsSourceCandidate>>.Failure(new WinPeDiagnostic(
                WinPeErrorCodes.OperatingSystemCatalogFetchFailed,
                "Failed to download the operating system catalog.",
                ex.Message,
                exception: ex));
        }
    }

    private async Task<WinPeResult<WinPeBootImagePreparationResult>> TryPrepareFromSourceAsync(
        WinPeBootImagePreparationOptions options,
        WindowsSourceCandidate candidate,
        CancellationToken cancellationToken)
    {
        string candidateName = PathSegment.Sanitize(candidate.RequestedEdition);
        string sourceDirectory = Path.Combine(options.Artifact.WorkingDirectoryPath, $"windows-source-{candidateName}");
        string exportDirectory = Path.Combine(sourceDirectory, "export");
        string mountDirectory = Path.Combine(sourceDirectory, "install-mount");
        string dependencyDirectory = Path.Combine(sourceDirectory, "boot-dependencies");
        string installWimPath = Path.Combine(exportDirectory, "install.wim");

        WinPeMountSession? session = null;
        try
        {
            DirectoryOperations.Recreate(sourceDirectory);
            Directory.CreateDirectory(exportDirectory);
            ReportProgress(options.Progress, 5, "Preparing Windows source package.");

            WinPeResult<string> sourcePathResult = await EnsureDownloadedAsync(
                options.CacheDirectoryPath,
                candidate.Source,
                options.DownloadProgress,
                cancellationToken).ConfigureAwait(false);

            if (!sourcePathResult.IsSuccess)
            {
                return WinPeResult<WinPeBootImagePreparationResult>.Failure(sourcePathResult.Error!);
            }

            ReportProgress(options.Progress, 16, "Resolving Windows image index.");
            WinPeResult<int> indexResult = await ResolveImageIndexAsync(
                options.Tools.DismPath,
                sourcePathResult.Value!,
                candidate.RequestedEdition,
                options.Artifact.WorkingDirectoryPath,
                CreateDismProgress(options.Progress, 16, "Resolving Windows image index."),
                cancellationToken).ConfigureAwait(false);

            if (!indexResult.IsSuccess)
            {
                return WinPeResult<WinPeBootImagePreparationResult>.Failure(indexResult.Error!);
            }

            ReportProgress(options.Progress, 19, "Exporting Windows image for boot image preparation.");
            WinPeProcessExecution exportResult = await WinPeDismProcessRunner.RunAsync(
                _processRunner,
                options.Tools.DismPath,
                $"/Export-Image /SourceImageFile:{WinPeProcessRunner.Quote(sourcePathResult.Value!)} /SourceIndex:{indexResult.Value} /DestinationImageFile:{WinPeProcessRunner.Quote(installWimPath)} /Compress:max /CheckIntegrity",
                options.Artifact.WorkingDirectoryPath,
                "Exporting Windows image with DISM.",
                CreateDismProgress(options.Progress, 19, "Exporting Windows image for boot image preparation."),
                cancellationToken).ConfigureAwait(false);

            if (!exportResult.IsSuccess)
            {
                return WinPeResult<WinPeBootImagePreparationResult>.Failure(exportResult.ToFailureDiagnostic(
                    WinPeErrorCodes.WinReExtractionFailed,
                    $"Failed to export the {candidate.RequestedEdition} image from the Windows source package.",
                    stage: "Export Windows source image",
                    toolName: "dism.exe"));
            }

            if (!File.Exists(installWimPath))
            {
                return WinPeResult<WinPeBootImagePreparationResult>.Failure(new WinPeDiagnostic(
                    WinPeErrorCodes.WinReExtractionFailed,
                    "DISM completed without producing the exported Windows image.",
                    exportResult.ToDiagnosticText(),
                    stage: "Export Windows source image",
                    exitCode: exportResult.ExitCode,
                    failureKind: WinPeFailureKinds.Process,
                    failureReason: WinPeFailureReasons.ArtifactMissing,
                    toolName: "dism.exe"));
            }

            ReportProgress(options.Progress, 24, "Mounting Windows source image.");
            WinPeResult<WinPeMountSession> mountResult = await WinPeMountSession.MountAsync(
                _processRunner,
                options.Tools.DismPath,
                installWimPath,
                mountDirectory,
                options.Artifact.WorkingDirectoryPath,
                cancellationToken,
                CreateDismProgress(options.Progress, 24, "Mounting Windows source image.")).ConfigureAwait(false);

            if (!mountResult.IsSuccess)
            {
                return WinPeResult<WinPeBootImagePreparationResult>.Failure(mountResult.Error!);
            }

            session = mountResult.Value!;
            string winRePath = Path.Combine(mountDirectory, "Windows", "System32", "Recovery", "winre.wim");
            if (options.BootImageSource == WinPeBootImageSource.WinReWifi && !File.Exists(winRePath))
            {
                return await FailWithDiscardAsync(
                    new WinPeDiagnostic(
                        WinPeErrorCodes.WinReExtractionFailed,
                        "The selected operating system image does not contain winre.wim.",
                        $"Expected path: '{winRePath}'."),
                    session,
                    cancellationToken).ConfigureAwait(false);
            }

            ReportProgress(options.Progress, 27, "Staging boot image dependencies.");
            WinPeResult<WinPeBootImagePreparationResult> dependencyResult = PrepareDependencyFiles(
                mountDirectory,
                dependencyDirectory,
                options.Artifact.Architecture,
                options.BootImageSource);

            if (!dependencyResult.IsSuccess)
            {
                return await FailWithDiscardAsync(dependencyResult.Error!, session, cancellationToken).ConfigureAwait(false);
            }

            if (options.BootImageSource == WinPeBootImageSource.WinReWifi)
            {
                ReportProgress(options.Progress, 29, "Replacing boot image with WinRE.");
                Directory.CreateDirectory(Path.GetDirectoryName(options.Artifact.BootWimPath)!);
                File.Copy(winRePath, options.Artifact.BootWimPath, overwrite: true);
            }

            WinPeResult discardResult = await session.DiscardAsync(cancellationToken).ConfigureAwait(false);
            session = null;
            if (!discardResult.IsSuccess)
            {
                return WinPeResult<WinPeBootImagePreparationResult>.Failure(discardResult.Error!);
            }

            TryDeleteDirectory(exportDirectory);
            TryDeleteDirectory(mountDirectory);
            ReportProgress(options.Progress, 30, "Boot image dependencies are ready.");
            return dependencyResult;
        }
        catch (Exception ex)
        {
            if (session is not null)
            {
                return await FailWithDiscardAsync(
                    new WinPeDiagnostic(
                        WinPeErrorCodes.WinReExtractionFailed,
                        "Failed to prepare boot image dependencies from the Windows source image.",
                        ex.Message,
                        exception: ex),
                    session,
                    cancellationToken).ConfigureAwait(false);
            }

            return WinPeResult<WinPeBootImagePreparationResult>.Failure(new WinPeDiagnostic(
                WinPeErrorCodes.WinReExtractionFailed,
                "Failed to prepare boot image dependencies from the Windows source image.",
                ex.Message,
                exception: ex));
        }
        finally
        {
            if (session is not null)
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task<WinPeResult<string>> EnsureDownloadedAsync(
        string cacheDirectoryPath,
        WindowsSourceCatalogItem source,
        IProgress<WinPeDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        string sourceCachePath = BuildCachedSourcePath(cacheDirectoryPath, source);
        Directory.CreateDirectory(Path.GetDirectoryName(sourceCachePath)!);
        string temporaryDownloadPath = $"{sourceCachePath}.{Guid.NewGuid():N}.download";

        if (File.Exists(sourceCachePath))
        {
            WinPeResult cachedHashResult = await ValidateHashIfRequestedAsync(
                sourceCachePath,
                source.Sha256,
                cancellationToken).ConfigureAwait(false);

            if (cachedHashResult.IsSuccess)
            {
                return WinPeResult<string>.Success(sourceCachePath);
            }

            TryDeleteFile(sourceCachePath);
        }

        if (!Uri.TryCreate(WindowsUpdateContentUrl.Normalize(source.Url), UriKind.Absolute, out Uri? sourceUri))
        {
            return WinPeResult<string>.Failure(
                WinPeErrorCodes.DownloadFailed,
                "The Windows source package URL is invalid.",
                source.Url);
        }

        try
        {
            ReportDownloadProgress(progress, 0, "Downloading Windows source package.");
            using HttpResponseMessage response = await _httpClient.GetAsync(
                sourceUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();
            long? totalBytes = response.Content.Headers.ContentLength;
            await using Stream sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (FileStream destinationStream = new(
                             temporaryDownloadPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             81920,
                             useAsync: true))
            {
                await CopyDownloadToFileAsync(
                    sourceStream,
                    destinationStream,
                    totalBytes,
                    progress,
                    cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryDownloadPath, sourceCachePath, overwrite: true);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            TryDeleteFile(sourceCachePath);
            TryDeleteFile(temporaryDownloadPath);
            return WinPeResult<string>.Failure(new WinPeDiagnostic(
                WinPeErrorCodes.DownloadFailed,
                "Failed to download the Windows source package.",
                ex.Message,
                exception: ex));
        }
        finally
        {
            TryDeleteFile(temporaryDownloadPath);
        }

        WinPeResult hashResult = await ValidateHashIfRequestedAsync(
            sourceCachePath,
            source.Sha256,
            cancellationToken).ConfigureAwait(false);

        if (!hashResult.IsSuccess)
        {
            TryDeleteFile(sourceCachePath);
            return WinPeResult<string>.Failure(hashResult.Error!);
        }

        return WinPeResult<string>.Success(sourceCachePath);
    }

    private static async Task CopyDownloadToFileAsync(
        Stream sourceStream,
        FileStream destinationStream,
        long? totalBytes,
        IProgress<WinPeDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        int lastReportedPercent = -1;
        long bytesWritten = await StreamCopy.CopyAsync(
            sourceStream,
            destinationStream,
            copiedBytes =>
            {
                double? percentage = TransferProgress.CalculatePercentage(copiedBytes, totalBytes);
                if (percentage.HasValue)
                {
                    int downloadPercent = (int)percentage.Value;
                    if (downloadPercent == lastReportedPercent)
                    {
                        return;
                    }

                    lastReportedPercent = downloadPercent;
                    ReportDownloadProgress(
                        progress,
                        downloadPercent,
                        $"Downloading Windows source package ({FormatBytes(copiedBytes)} / {FormatBytes(totalBytes.GetValueOrDefault())}).");
                    return;
                }

                ReportDownloadProgress(
                    progress,
                    null,
                    $"Downloading Windows source package ({FormatBytes(copiedBytes)}).");
            },
            cancellationToken).ConfigureAwait(false);

        if (totalBytes is > 0)
        {
            ReportDownloadProgress(
                progress,
                100,
                $"Downloading Windows source package ({FormatBytes(bytesWritten)} / {FormatBytes(totalBytes.Value)}).");
        }
    }

    private async Task<WinPeResult<int>> ResolveImageIndexAsync(
        string dismPath,
        string sourceImagePath,
        string requestedEdition,
        string workingDirectory,
        IProgress<WinPeDismProgress>? dismProgress,
        CancellationToken cancellationToken)
    {
        WinPeProcessExecution imageInfoResult = await WinPeDismProcessRunner.RunAsync(
            _processRunner,
            dismPath,
            $"/Get-ImageInfo /ImageFile:{WinPeProcessRunner.Quote(sourceImagePath)}",
            workingDirectory,
            "Resolving Windows image index with DISM.",
            dismProgress,
            cancellationToken).ConfigureAwait(false);

        if (!imageInfoResult.IsSuccess)
        {
            return WinPeResult<int>.Failure(imageInfoResult.ToFailureDiagnostic(
                WinPeErrorCodes.WinReIndexResolutionFailed,
                "Failed to inspect the Windows source package image indexes.",
                stage: "Inspect Windows source image",
                toolName: "dism.exe"));
        }

        return ResolveImageIndexFromOutput(imageInfoResult.StandardOutput, requestedEdition);
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

        return null;
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

    private static async Task<WinPeResult<WinPeBootImagePreparationResult>> FailWithDiscardAsync(
        WinPeDiagnostic primaryDiagnostic,
        WinPeMountSession session,
        CancellationToken cancellationToken)
    {
        WinPeResult discardResult = await session.DiscardAsync(cancellationToken).ConfigureAwait(false);
        if (discardResult.IsSuccess)
        {
            return WinPeResult<WinPeBootImagePreparationResult>.Failure(primaryDiagnostic);
        }

        string details = string.Join(
            Environment.NewLine,
            primaryDiagnostic.Details ?? string.Empty,
            "Discard diagnostics:",
            discardResult.Error?.Details ?? string.Empty).Trim();

        return WinPeResult<WinPeBootImagePreparationResult>.Failure(primaryDiagnostic with { Details = details });
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
            Edition = ReadElement(item, "Edition"),
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

    private static void ReportDownloadProgress(IProgress<WinPeDownloadProgress>? progress, int? percent, string status)
    {
        progress?.Report(new WinPeDownloadProgress
        {
            Percent = percent.HasValue
                ? Math.Clamp(percent.Value, 0, 100)
                : null,
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

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? $"{bytes} {units[unitIndex]}"
            : $"{value:F1} {units[unitIndex]}";
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
            .OrderBy(item => GetLicenseChannelOrder(item.LicenseChannel))
            .ThenBy(item => GetClientTypeOrder(item.ClientType))
            .ThenByDescending(item => item.BuildMajor)
            .ThenByDescending(item => item.BuildUbr)
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

    private static IReadOnlyList<ImageIndexDescriptor> ParseImageDescriptors(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        var descriptors = new List<ImageIndexDescriptor>();
        ImageIndexDescriptor? current = null;

        foreach (string line in output.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            Match indexMatch = Regex.Match(line, @"^\s*Index\s*:\s*(\d+)\s*$", RegexOptions.IgnoreCase);
            if (indexMatch.Success)
            {
                if (current is not null)
                {
                    descriptors.Add(current);
                }

                current = new ImageIndexDescriptor
                {
                    Index = int.Parse(indexMatch.Groups[1].Value, CultureInfo.InvariantCulture),
                    Name = string.Empty,
                    Edition = string.Empty,
                    EditionId = string.Empty
                };

                continue;
            }

            if (current is null)
            {
                continue;
            }

            Match nameMatch = Regex.Match(line, @"^\s*Name\s*:\s*(.+)\s*$", RegexOptions.IgnoreCase);
            if (nameMatch.Success)
            {
                current = current with { Name = nameMatch.Groups[1].Value.Trim() };
                continue;
            }

            Match editionMatch = Regex.Match(line, @"^\s*Edition\s*:\s*(.+)\s*$", RegexOptions.IgnoreCase);
            if (editionMatch.Success)
            {
                current = current with { Edition = editionMatch.Groups[1].Value.Trim() };
                continue;
            }

            Match editionIdMatch = Regex.Match(line, @"^\s*Edition\s+ID\s*:\s*(.+)\s*$", RegexOptions.IgnoreCase);
            if (editionIdMatch.Success)
            {
                current = current with { EditionId = editionIdMatch.Groups[1].Value.Trim() };
            }
        }

        if (current is not null)
        {
            descriptors.Add(current);
        }

        return descriptors;
    }

    private static bool ContainsNormalized(string source, string expected)
    {
        string normalized = NormalizeToken(source);
        if (normalized.Length == 0 || expected.Length == 0)
        {
            return false;
        }

        return normalized.Contains(expected, StringComparison.OrdinalIgnoreCase) ||
               expected.Contains(normalized, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        char[] filtered = value
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray();

        return new string(filtered);
    }

    private sealed record ImageIndexDescriptor
    {
        public required int Index { get; init; }
        public required string Name { get; init; }
        public required string Edition { get; init; }
        public required string EditionId { get; init; }
    }
}
