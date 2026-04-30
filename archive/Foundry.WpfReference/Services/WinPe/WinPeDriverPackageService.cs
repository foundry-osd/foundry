using System.Net.Http;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Foundry.Services.WinPe;

internal sealed record WinPePreparedDriverSet
{
    public IReadOnlyList<string> ExtractionDirectories { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> DownloadedPackagePaths { get; init; } = Array.Empty<string>();
}

internal sealed class WinPeDriverPackageService
{
    private static readonly HttpClient HttpClient = CreateHttpClient();

    private readonly WinPeProcessRunner _processRunner;
    private readonly ILogger<WinPeDriverPackageService> _logger;

    public WinPeDriverPackageService(WinPeProcessRunner processRunner, ILogger<WinPeDriverPackageService> logger)
    {
        _processRunner = processRunner;
        _logger = logger;
    }

    private static HttpClient CreateHttpClient()
    {
        HttpClient client = new()
        {
            Timeout = TimeSpan.FromMinutes(30)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Foundry/1.0");
        return client;
    }

    public async Task<WinPeResult<WinPePreparedDriverSet>> PrepareAsync(
        IReadOnlyList<WinPeDriverCatalogEntry> packages,
        string downloadRootPath,
        string extractRootPath,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Preparing {PackageCount} WinPE driver packages. DownloadRootPath={DownloadRootPath}, ExtractRootPath={ExtractRootPath}",
            packages.Count,
            downloadRootPath,
            extractRootPath);

        Directory.CreateDirectory(downloadRootPath);
        Directory.CreateDirectory(extractRootPath);

        var extractedDirectories = new List<string>(packages.Count);
        var downloadedFiles = new List<string>(packages.Count);

        for (int index = 0; index < packages.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            WinPeDriverCatalogEntry package = packages[index];
            _logger.LogInformation("Processing WinPE driver package {PackageIndex}/{PackageCount}. Vendor={Vendor}, Id={PackageId}, Version={PackageVersion}",
                index + 1,
                packages.Count,
                package.Vendor,
                package.Id,
                package.Version);

            string fileName = ResolvePackageFileName(package);
            string downloadPath = Path.Combine(downloadRootPath, fileName);

            WinPeResult downloadResult = await DownloadPackageAsync(package.DownloadUri, downloadPath, cancellationToken).ConfigureAwait(false);
            if (!downloadResult.IsSuccess)
            {
                _logger.LogWarning("Failed to download driver package. Id={PackageId}, Code={ErrorCode}, Message={ErrorMessage}",
                    package.Id,
                    downloadResult.Error?.Code,
                    downloadResult.Error?.Message);
                return WinPeResult<WinPePreparedDriverSet>.Failure(downloadResult.Error!);
            }

            downloadedFiles.Add(downloadPath);

            WinPeResult hashValidationResult = await ValidateSha256Async(package, downloadPath, cancellationToken).ConfigureAwait(false);
            if (!hashValidationResult.IsSuccess)
            {
                _logger.LogWarning("SHA256 validation failed for package Id={PackageId}. Code={ErrorCode}, Message={ErrorMessage}",
                    package.Id,
                    hashValidationResult.Error?.Code,
                    hashValidationResult.Error?.Message);
                return WinPeResult<WinPePreparedDriverSet>.Failure(hashValidationResult.Error!);
            }

            string normalizedFolderName = $"{index + 1:D2}_{WinPeFileSystemHelper.SanitizePathSegment(package.Vendor.ToString())}_{WinPeFileSystemHelper.SanitizePathSegment(package.Id)}";
            string extractPath = Path.Combine(extractRootPath, normalizedFolderName);
            WinPeFileSystemHelper.EnsureDirectoryClean(extractPath);

            WinPeResult extractionResult = await ExtractPackageAsync(downloadPath, extractPath, cancellationToken).ConfigureAwait(false);
            if (!extractionResult.IsSuccess)
            {
                _logger.LogWarning("Driver package extraction failed. Id={PackageId}, Code={ErrorCode}, Message={ErrorMessage}",
                    package.Id,
                    extractionResult.Error?.Code,
                    extractionResult.Error?.Message);
                return WinPeResult<WinPePreparedDriverSet>.Failure(extractionResult.Error!);
            }

            extractedDirectories.Add(extractPath);
        }

        _logger.LogInformation("WinPE driver package preparation completed. Extracted={ExtractedCount}, Downloaded={DownloadedCount}",
            extractedDirectories.Count,
            downloadedFiles.Count);
        return WinPeResult<WinPePreparedDriverSet>.Success(new WinPePreparedDriverSet
        {
            ExtractionDirectories = extractedDirectories,
            DownloadedPackagePaths = downloadedFiles
        });
    }

    private static string ResolvePackageFileName(WinPeDriverCatalogEntry package)
    {
        if (!string.IsNullOrWhiteSpace(package.FileName))
        {
            return WinPeFileSystemHelper.SanitizePathSegment(package.FileName);
        }

        if (Uri.TryCreate(package.DownloadUri, UriKind.Absolute, out Uri? uri))
        {
            string candidate = Path.GetFileName(uri.LocalPath);
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return WinPeFileSystemHelper.SanitizePathSegment(candidate);
            }
        }

        string extension = package.Format.ToLowerInvariant() switch
        {
            "cab" => ".cab",
            "zip" => ".zip",
            _ => ".exe"
        };

        return $"{WinPeFileSystemHelper.SanitizePathSegment(package.Id)}{extension}";
    }


    private async Task<WinPeResult> DownloadPackageAsync(
        string sourceUri,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Downloading driver package from {SourceUri} to {DestinationPath}.", sourceUri, destinationPath);
            using HttpResponseMessage response = await HttpClient.GetAsync(sourceUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return WinPeResult.Failure(
                    WinPeErrorCodes.DownloadFailed,
                    "Driver package download failed.",
                    $"URI: '{sourceUri}', HTTP status: {(int)response.StatusCode} {response.ReasonPhrase}");
            }

            await using Stream sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using FileStream destinationStream = new(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await sourceStream.CopyToAsync(destinationStream, cancellationToken).ConfigureAwait(false);
            _logger.LogDebug("Downloaded driver package to {DestinationPath}.", destinationPath);
            return WinPeResult.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download driver package from {SourceUri}.", sourceUri);
            return WinPeResult.Failure(
                WinPeErrorCodes.DownloadFailed,
                "Failed to download driver package.",
                $"URI: '{sourceUri}'. Error: {ex.Message}");
        }
    }

    private static async Task<WinPeResult> ValidateSha256Async(
        WinPeDriverCatalogEntry package,
        string filePath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(package.Sha256))
        {
            return WinPeResult.Success();
        }

        string expected = package.Sha256.Trim().Replace("-", string.Empty, StringComparison.OrdinalIgnoreCase).ToUpperInvariant();
        string actual = await WinPeHashHelper.ComputeSha256Async(filePath, cancellationToken).ConfigureAwait(false);
        if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            return WinPeResult.Success();
        }

        return WinPeResult.Failure(
            WinPeErrorCodes.HashMismatch,
            "Driver package hash verification failed.",
            $"File: '{filePath}', Expected SHA256: '{expected}', Actual SHA256: '{actual}'.");
    }

    private async Task<WinPeResult> ExtractPackageAsync(
        string packagePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Extracting driver package. PackagePath={PackagePath}, DestinationPath={DestinationPath}", packagePath, destinationPath);
        string extension = Path.GetExtension(packagePath);

        if (!extension.Equals(".cab", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return WinPeResult.Failure(
                WinPeErrorCodes.DriverExtractionFailed,
                "Unsupported driver package format.",
                $"File: '{packagePath}', extension: '{extension}'. Supported: .cab, .exe, .zip");
        }

        WinPeResult extractionResult = await ExtractArchiveWithSevenZipAsync(packagePath, destinationPath, cancellationToken).ConfigureAwait(false);
        if (!extractionResult.IsSuccess)
        {
            return extractionResult;
        }

        if (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) &&
            !WinPeFileSystemHelper.ContainsFileRecursive(destinationPath, "*.inf"))
        {
            return WinPeResult.Failure(
                WinPeErrorCodes.DriverExtractionFailed,
                "Executable driver package was extracted with 7-Zip but no INF files were found.",
                $"Archive: '{packagePath}', destination: '{destinationPath}'.");
        }

        return WinPeResult.Success();
    }

    private async Task<WinPeResult> ExtractArchiveWithSevenZipAsync(
        string archivePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        WinPeResult<string> sevenZipExecutablePathResult = ResolveBundledSevenZipExecutablePath();
        if (!sevenZipExecutablePathResult.IsSuccess)
        {
            return WinPeResult.Failure(sevenZipExecutablePathResult.Error!);
        }

        string sevenZipExecutablePath = sevenZipExecutablePathResult.Value!;
        WinPeProcessExecution extractionResult = await _processRunner.RunAsync(
            sevenZipExecutablePath,
            $"x -y -o{WinPeProcessRunner.Quote(destinationPath)} {WinPeProcessRunner.Quote(archivePath)}",
            destinationPath,
            cancellationToken).ConfigureAwait(false);

        if (!extractionResult.IsSuccess)
        {
            return WinPeResult.Failure(
                WinPeErrorCodes.DriverExtractionFailed,
                "Failed to extract driver package with bundled 7-Zip.",
                extractionResult.ToDiagnosticText());
        }

        return WinPeResult.Success();
    }

    private static WinPeResult<string> ResolveBundledSevenZipExecutablePath()
    {
        string runtimeFolderName = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            _ => "x64"
        };

        string executablePath = Path.Combine(
            AppContext.BaseDirectory,
            WinPeDefaults.BundledSevenZipRelativePath,
            runtimeFolderName,
            "7za.exe");

        if (!File.Exists(executablePath))
        {
            return WinPeResult<string>.Failure(
                WinPeErrorCodes.ToolNotFound,
                "Bundled 7-Zip executable was not found.",
                $"Expected file: '{executablePath}'. Ensure Assets\\7z is copied to output.");
        }

        return WinPeResult<string>.Success(executablePath);
    }
}
