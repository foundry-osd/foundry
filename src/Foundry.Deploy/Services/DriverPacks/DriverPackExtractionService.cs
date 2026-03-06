using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.System;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.Services.DriverPacks;

public sealed class DriverPackExtractionService : IDriverPackExtractionService
{
    private readonly IMicrosoftUpdateCatalogDriverService _microsoftUpdateCatalogDriverService;
    private readonly IProcessRunner _processRunner;
    private readonly ILogger<DriverPackExtractionService> _logger;

    public DriverPackExtractionService(
        IMicrosoftUpdateCatalogDriverService microsoftUpdateCatalogDriverService,
        IProcessRunner processRunner,
        ILogger<DriverPackExtractionService> logger)
    {
        _microsoftUpdateCatalogDriverService = microsoftUpdateCatalogDriverService;
        _processRunner = processRunner;
        _logger = logger;
    }

    public async Task<DriverPackExtractionResult> ExtractAsync(
        DriverPackExecutionPlan executionPlan,
        string extractionRootPath,
        CancellationToken cancellationToken = default,
        IProgress<double>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(executionPlan);

        Directory.CreateDirectory(extractionRootPath);
        progress?.Report(0d);

        if (executionPlan.InstallMode == DriverPackInstallMode.None)
        {
            progress?.Report(100d);
            return new DriverPackExtractionResult
            {
                ExecutionPlan = executionPlan,
                ExtractedDirectoryPath = null,
                InfCount = 0,
                Message = "Driver pack extraction skipped."
            };
        }

        if (executionPlan.InstallMode == DriverPackInstallMode.DeferredSetupComplete)
        {
            progress?.Report(25d);
            progress?.Report(75d);
            progress?.Report(100d);
            return new DriverPackExtractionResult
            {
                ExecutionPlan = executionPlan,
                ExtractedDirectoryPath = null,
                InfCount = 0,
                Message = "Driver pack does not require WinPE extraction; deferred installation will be staged."
            };
        }

        string packageFolderName = executionPlan.ExtractionMethod == DriverPackExtractionMethod.MicrosoftUpdateCatalogExpand
            ? "MicrosoftUpdateCatalog"
            : SanitizePathSegment(Path.GetFileNameWithoutExtension(executionPlan.DownloadedPath));
        string extractedPath = Path.Combine(extractionRootPath, packageFolderName);
        ResetDirectory(extractedPath);

        _logger.LogInformation(
            "Extracting driver pack. InstallMode={InstallMode}, ExtractionMethod={ExtractionMethod}, DownloadedPath={DownloadedPath}, ExtractedPath={ExtractedPath}",
            executionPlan.InstallMode,
            executionPlan.ExtractionMethod,
            executionPlan.DownloadedPath,
            extractedPath);

        switch (executionPlan.ExtractionMethod)
        {
            case DriverPackExtractionMethod.SevenZip:
                await ExtractWithSevenZipAsync(executionPlan.DownloadedPath, extractedPath, extractionRootPath, cancellationToken, progress)
                    .ConfigureAwait(false);
                break;

            case DriverPackExtractionMethod.DellSelfExtractor:
                await ExtractDellSelfExtractorAsync(executionPlan.DownloadedPath, extractedPath, extractionRootPath, cancellationToken, progress)
                    .ConfigureAwait(false);
                break;

            case DriverPackExtractionMethod.MicrosoftUpdateCatalogExpand:
            {
                MicrosoftUpdateCatalogDriverResult microsoftResult = await _microsoftUpdateCatalogDriverService
                    .ExpandAsync(executionPlan.DownloadedPath, extractedPath, cancellationToken, progress)
                    .ConfigureAwait(false);

                progress?.Report(100d);
                return new DriverPackExtractionResult
                {
                    ExecutionPlan = executionPlan,
                    ExtractedDirectoryPath = microsoftResult.DestinationDirectory,
                    InfCount = microsoftResult.InfCount,
                    Message = microsoftResult.Message
                };
            }

            default:
                throw new InvalidOperationException(
                    $"Unsupported extraction method '{executionPlan.ExtractionMethod}'.");
        }

        int infCount = Directory
            .EnumerateFiles(extractedPath, "*.inf", SearchOption.AllDirectories)
            .Count();

        if (executionPlan.RequiresInfPayload && infCount == 0)
        {
            throw new InvalidOperationException(
                $"Driver pack extraction completed but no INF files were found in '{extractedPath}'.");
        }

        progress?.Report(100d);

        return new DriverPackExtractionResult
        {
            ExecutionPlan = executionPlan,
            ExtractedDirectoryPath = extractedPath,
            InfCount = infCount,
            Message = infCount > 0
                ? $"Driver pack extracted successfully: {infCount} INF files."
                : "Driver pack extracted successfully."
        };
    }

    private async Task ExtractWithSevenZipAsync(
        string archivePath,
        string extractedPath,
        string workingDirectory,
        CancellationToken cancellationToken,
        IProgress<double>? progress)
    {
        string sevenZipPath = ResolveSevenZipExecutablePath();
        progress?.Report(5d);

        SevenZipProgressReporter? reporter = progress is null ? null : new(progress);
        Action<string>? onOutput = reporter is null ? null : reporter.HandleOutput;
        Action<string>? onError = reporter is null ? null : reporter.HandleOutput;
        ProcessExecutionResult execution = await _processRunner
            .RunAsync(
                sevenZipPath,
                [
                    "x",
                    "-y",
                    "-bsp1",
                    $"-o{extractedPath}",
                    archivePath
                ],
                workingDirectory,
                onOutput,
                onError,
                cancellationToken)
            .ConfigureAwait(false);

        if (!execution.IsSuccess)
        {
            throw new InvalidOperationException(
                $"7-Zip extraction failed for '{archivePath}'.{Environment.NewLine}{ToDiagnostic(execution)}");
        }

        progress?.Report(95d);
    }

    private async Task ExtractDellSelfExtractorAsync(
        string packagePath,
        string extractedPath,
        string workingDirectory,
        CancellationToken cancellationToken,
        IProgress<double>? progress)
    {
        progress?.Report(10d);
        ProcessExecutionResult execution = await _processRunner
            .RunAsync(
                packagePath,
                [
                    "/s",
                    $"/e={extractedPath}"
                ],
                workingDirectory,
                cancellationToken)
            .ConfigureAwait(false);

        if (!execution.IsSuccess)
        {
            throw new InvalidOperationException(
                $"Dell driver pack extraction failed for '{packagePath}'.{Environment.NewLine}{ToDiagnostic(execution)}");
        }

        progress?.Report(95d);
    }

    private static string ResolveSevenZipExecutablePath()
    {
        string winPePath = Path.Combine(Environment.SystemDirectory, "7za.exe");
        if (File.Exists(winPePath))
        {
            return winPePath;
        }

        string runtimeFolder = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            _ => "x64"
        };

        string architectureSpecific = Path.Combine(AppContext.BaseDirectory, "Assets", "7z", runtimeFolder, "7za.exe");
        if (File.Exists(architectureSpecific))
        {
            return architectureSpecific;
        }

        string bundledPath = Path.Combine(AppContext.BaseDirectory, "Assets", "7z", "7za.exe");
        if (File.Exists(bundledPath))
        {
            return bundledPath;
        }

        throw new FileNotFoundException(
            "No usable 7-Zip executable was found. Expected a WinPE copy in System32 or the bundled Assets\\7z payload.",
            architectureSpecific);
    }

    private static void ResetDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }

        Directory.CreateDirectory(path);
    }

    private static string SanitizePathSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "driverpack";
        }

        char[] invalid = Path.GetInvalidFileNameChars();
        string sanitized = new(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return sanitized.Trim().TrimEnd('.');
    }

    private static string ToDiagnostic(ProcessExecutionResult execution)
    {
        return
            $"ExitCode={execution.ExitCode}{Environment.NewLine}" +
            $"StdOut:{Environment.NewLine}{execution.StandardOutput}{Environment.NewLine}" +
            $"StdErr:{Environment.NewLine}{execution.StandardError}";
    }
}
