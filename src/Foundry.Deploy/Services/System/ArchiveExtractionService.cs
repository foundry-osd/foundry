using System.IO;
using System.Runtime.InteropServices;

namespace Foundry.Deploy.Services.System;

public sealed class ArchiveExtractionService : IArchiveExtractionService
{
    private readonly IProcessRunner _processRunner;

    public ArchiveExtractionService(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public async Task ExtractWithSevenZipAsync(
        string archivePath,
        string extractedPath,
        string workingDirectory,
        CancellationToken cancellationToken = default,
        IProgress<double>? progress = null)
    {
        if (string.IsNullOrWhiteSpace(archivePath))
        {
            throw new ArgumentException("Archive path is required.", nameof(archivePath));
        }

        if (string.IsNullOrWhiteSpace(extractedPath))
        {
            throw new ArgumentException("Extraction path is required.", nameof(extractedPath));
        }

        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            throw new ArgumentException("Working directory is required.", nameof(workingDirectory));
        }

        string sevenZipPath = ResolveSevenZipExecutablePath();
        Directory.CreateDirectory(extractedPath);
        progress?.Report(0d);

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

        progress?.Report(100d);
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

    private static string ToDiagnostic(ProcessExecutionResult execution)
    {
        return
            $"ExitCode={execution.ExitCode}{Environment.NewLine}" +
            $"StdOut:{Environment.NewLine}{execution.StandardOutput}{Environment.NewLine}" +
            $"StdErr:{Environment.NewLine}{execution.StandardError}";
    }
}
