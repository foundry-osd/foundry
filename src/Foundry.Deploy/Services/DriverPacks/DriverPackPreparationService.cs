using System.IO;
using System.Runtime.InteropServices;
using Foundry.Deploy.Models;
using Foundry.Deploy.Services.System;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.Services.DriverPacks;

public sealed class DriverPackPreparationService : IDriverPackPreparationService
{
    private readonly IProcessRunner _processRunner;
    private readonly ILogger<DriverPackPreparationService> _logger;

    public DriverPackPreparationService(IProcessRunner processRunner, ILogger<DriverPackPreparationService> logger)
    {
        _processRunner = processRunner;
        _logger = logger;
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

        _logger.LogInformation("Preparing driver pack. DriverPackId={DriverPackId}, ArchivePath={ArchivePath}, ExtractionRootPath={ExtractionRootPath}",
            driverPack.Id,
            archivePath,
            extractionRootPath);
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
            case ".zip":
                await ExtractWithSevenZipAsync(archivePath, extractedPath, extractionRootPath, cancellationToken).ConfigureAwait(false);
                DriverPackPreparationResult archiveResult = BuildResult(archivePath, extractedPath);
                _logger.LogInformation("Driver pack extraction completed. ExtractedDirectoryPath={ExtractedDirectoryPath}, RequiresDeferredInstall={RequiresDeferredInstall}",
                    archiveResult.ExtractedDirectoryPath,
                    archiveResult.RequiresDeferredInstall);
                return archiveResult;

            case ".exe":
            case ".msi":
                _logger.LogWarning("Driver pack requires deferred install (installer format). Extension={Extension}, ArchivePath={ArchivePath}", extension, archivePath);
                return new DriverPackPreparationResult
                {
                    ArchivePath = archivePath,
                    ExtractedDirectoryPath = null,
                    RequiresDeferredInstall = true,
                    Message = "Executable package requires deferred installation in deployed OS."
                };

            default:
                _logger.LogWarning("Driver pack uses unsupported extraction format. Extension={Extension}, ArchivePath={ArchivePath}", extension, archivePath);
                return new DriverPackPreparationResult
                {
                    ArchivePath = archivePath,
                    ExtractedDirectoryPath = null,
                    RequiresDeferredInstall = true,
                    Message = $"Unsupported extraction format '{extension}'. Deferred installation required."
                };
        }
    }

    private async Task ExtractWithSevenZipAsync(
        string archivePath,
        string extractedPath,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        string sevenZipPath = ResolveBundledSevenZipExecutablePath();
        ProcessExecutionResult execution = await _processRunner
            .RunAsync(
                sevenZipPath,
                $"x -y -o{Quote(extractedPath)} {Quote(archivePath)}",
                workingDirectory,
                cancellationToken)
            .ConfigureAwait(false);

        if (!execution.IsSuccess)
        {
            _logger.LogError("7-Zip extraction failed. ArchivePath={ArchivePath}, ExitCode={ExitCode}, StdErr={StdErr}",
                archivePath,
                execution.ExitCode,
                execution.StandardError);
            throw new InvalidOperationException(
                $"7-Zip extraction failed (ExitCode={execution.ExitCode}). StdErr: {execution.StandardError}");
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

    private static string ResolveBundledSevenZipExecutablePath()
    {
        string runtimeFolder = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            _ => "x64"
        };

        string executablePath = Path.Combine(AppContext.BaseDirectory, "Assets", "7z", runtimeFolder, "7za.exe");
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException(
                "Bundled 7-Zip executable was not found. Expected Assets\\7z to be copied to output.",
                executablePath);
        }

        return executablePath;
    }

    private static string Quote(string value)
    {
        return value.Contains(' ', StringComparison.Ordinal)
            ? $"\"{value}\""
            : value;
    }
}
