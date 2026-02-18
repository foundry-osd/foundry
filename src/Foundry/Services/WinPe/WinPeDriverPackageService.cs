using System.Net.Http;

namespace Foundry.Services.WinPe;

internal sealed record WinPePreparedDriverSet
{
    public IReadOnlyList<string> ExtractionDirectories { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> DownloadedPackagePaths { get; init; } = Array.Empty<string>();
}

internal sealed class WinPeDriverPackageService
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(30)
    };

    private readonly WinPeProcessRunner _processRunner;

    public WinPeDriverPackageService(WinPeProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public async Task<WinPeResult<WinPePreparedDriverSet>> PrepareAsync(
        IReadOnlyList<WinPeDriverCatalogEntry> packages,
        string downloadRootPath,
        string extractRootPath,
        WinPeToolPaths tools,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(downloadRootPath);
        Directory.CreateDirectory(extractRootPath);

        var extractedDirectories = new List<string>(packages.Count);
        var downloadedFiles = new List<string>(packages.Count);

        for (int index = 0; index < packages.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            WinPeDriverCatalogEntry package = packages[index];
            string fileName = ResolvePackageFileName(package);
            string downloadPath = Path.Combine(downloadRootPath, fileName);

            WinPeResult downloadResult = await DownloadPackageAsync(package.DownloadUri, downloadPath, cancellationToken).ConfigureAwait(false);
            if (!downloadResult.IsSuccess)
            {
                return WinPeResult<WinPePreparedDriverSet>.Failure(downloadResult.Error!);
            }

            downloadedFiles.Add(downloadPath);

            WinPeResult hashValidationResult = await ValidateSha256Async(package, downloadPath, cancellationToken).ConfigureAwait(false);
            if (!hashValidationResult.IsSuccess)
            {
                return WinPeResult<WinPePreparedDriverSet>.Failure(hashValidationResult.Error!);
            }

            string normalizedFolderName = $"{index + 1:D2}_{WinPeFileSystemHelper.SanitizePathSegment(package.Vendor.ToString())}_{WinPeFileSystemHelper.SanitizePathSegment(package.Id)}";
            string extractPath = Path.Combine(extractRootPath, normalizedFolderName);
            WinPeFileSystemHelper.EnsureDirectoryClean(extractPath);

            WinPeResult extractionResult = await ExtractPackageAsync(downloadPath, extractPath, tools, cancellationToken).ConfigureAwait(false);
            if (!extractionResult.IsSuccess)
            {
                return WinPeResult<WinPePreparedDriverSet>.Failure(extractionResult.Error!);
            }

            extractedDirectories.Add(extractPath);
        }

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

        string extension = package.Format.Equals("cab", StringComparison.OrdinalIgnoreCase)
            ? ".cab"
            : ".exe";

        return $"{WinPeFileSystemHelper.SanitizePathSegment(package.Id)}{extension}";
    }

    private static async Task<WinPeResult> DownloadPackageAsync(
        string sourceUri,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        try
        {
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
            return WinPeResult.Success();
        }
        catch (Exception ex)
        {
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
        WinPeToolPaths tools,
        CancellationToken cancellationToken)
    {
        string extension = Path.GetExtension(packagePath);

        if (extension.Equals(".cab", StringComparison.OrdinalIgnoreCase))
        {
            return await ExtractCabAsync(packagePath, destinationPath, tools, cancellationToken).ConfigureAwait(false);
        }

        if (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return await ExtractExecutableAsync(packagePath, destinationPath, cancellationToken).ConfigureAwait(false);
        }

        return WinPeResult.Failure(
            WinPeErrorCodes.DriverExtractionFailed,
            "Unsupported driver package format.",
            $"File: '{packagePath}', extension: '{extension}'. Supported: .cab, .exe");
    }

    private async Task<WinPeResult> ExtractCabAsync(
        string cabPath,
        string destinationPath,
        WinPeToolPaths tools,
        CancellationToken cancellationToken)
    {
        WinPeProcessExecution expandResult = await _processRunner.RunAsync(
            tools.ExpandPath,
            $"-F:* {WinPeProcessRunner.Quote(cabPath)} {WinPeProcessRunner.Quote(destinationPath)}",
            destinationPath,
            cancellationToken).ConfigureAwait(false);

        if (!expandResult.IsSuccess)
        {
            return WinPeResult.Failure(
                WinPeErrorCodes.DriverExtractionFailed,
                "Failed to extract CAB driver package.",
                expandResult.ToDiagnosticText());
        }

        return WinPeResult.Success();
    }

    private async Task<WinPeResult> ExtractExecutableAsync(
        string executablePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        // HP SoftPaq extraction switches differ by package generation; try common variants.
        string[] argumentCandidates =
        [
            $"/s /e /f {WinPeProcessRunner.Quote(destinationPath)}",
            $"/s /f {WinPeProcessRunner.Quote(destinationPath)}",
            $"/e /s /f {WinPeProcessRunner.Quote(destinationPath)}",
            $"/s /extract={WinPeProcessRunner.Quote(destinationPath)}",
            $"/s /x:{WinPeProcessRunner.Quote(destinationPath)}"
        ];

        WinPeProcessExecution? lastResult = null;

        foreach (string args in argumentCandidates)
        {
            WinPeProcessExecution result = await _processRunner.RunAsync(
                executablePath,
                args,
                destinationPath,
                cancellationToken).ConfigureAwait(false);

            lastResult = result;
            if (result.IsSuccess && WinPeFileSystemHelper.ContainsFileRecursive(destinationPath, "*.inf"))
            {
                return WinPeResult.Success();
            }
        }

        string details = lastResult?.ToDiagnosticText() ?? "No extraction attempt executed.";
        return WinPeResult.Failure(
            WinPeErrorCodes.DriverExtractionFailed,
            "Failed to extract executable driver package.",
            details);
    }
}
