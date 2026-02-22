using System.IO;
using System.IO.Compression;
using Foundry.Deploy.Models;
using Foundry.Deploy.Services.System;

namespace Foundry.Deploy.Services.DriverPacks;

public sealed class DriverPackPreparationService : IDriverPackPreparationService
{
    private readonly IProcessRunner _processRunner;

    public DriverPackPreparationService(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public async Task<DriverPackPreparationResult> PrepareAsync(
        DriverPackCatalogItem driverPack,
        string archivePath,
        string extractionRootPath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException("Driver pack archive not found.", archivePath);
        }

        Directory.CreateDirectory(extractionRootPath);
        string extension = Path.GetExtension(archivePath).ToLowerInvariant();
        string packageFolderName = SanitizePathSegment(driverPack.Id.Length > 0 ? driverPack.Id : driverPack.FileName);
        string extractedPath = Path.Combine(extractionRootPath, packageFolderName);

        if (Directory.Exists(extractedPath))
        {
            Directory.Delete(extractedPath, recursive: true);
        }

        Directory.CreateDirectory(extractedPath);

        switch (extension)
        {
            case ".cab":
                {
                    string expandExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "expand.exe");
                    ProcessExecutionResult execution = await _processRunner
                        .RunAsync(expandExe, $"-F:* \"{archivePath}\" \"{extractedPath}\"", extractionRootPath, cancellationToken)
                        .ConfigureAwait(false);
                    if (!execution.IsSuccess)
                    {
                        throw new InvalidOperationException($"CAB extraction failed: {execution.StandardError}");
                    }

                    return BuildResult(archivePath, extractedPath);
                }

            case ".zip":
                ZipFile.ExtractToDirectory(archivePath, extractedPath, overwriteFiles: true);
                return BuildResult(archivePath, extractedPath);

            case ".exe":
            case ".msi":
                return new DriverPackPreparationResult
                {
                    ArchivePath = archivePath,
                    ExtractedDirectoryPath = null,
                    RequiresDeferredInstall = true,
                    Message = "Executable package requires deferred installation in deployed OS."
                };

            default:
                return new DriverPackPreparationResult
                {
                    ArchivePath = archivePath,
                    ExtractedDirectoryPath = null,
                    RequiresDeferredInstall = true,
                    Message = $"Unsupported extraction format '{extension}'. Deferred installation required."
                };
        }
    }

    private static DriverPackPreparationResult BuildResult(string archivePath, string extractedPath)
    {
        bool hasInf = Directory.EnumerateFiles(extractedPath, "*.inf", SearchOption.AllDirectories).Any();
        return new DriverPackPreparationResult
        {
            ArchivePath = archivePath,
            ExtractedDirectoryPath = hasInf ? extractedPath : null,
            RequiresDeferredInstall = !hasInf,
            Message = hasInf
                ? "Driver pack extracted and contains INF files."
                : "Driver pack extracted but no INF files found; deferred install required."
        };
    }

    private static string SanitizePathSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "package";
        }

        string sanitized = new(value.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch).ToArray());
        return sanitized.Trim().TrimEnd('.');
    }
}
