using System.Text;
using System.IO;
using Foundry.Deploy.Services.System;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.Services.DriverPacks;

public sealed class MicrosoftUpdateCatalogDriverService : IMicrosoftUpdateCatalogDriverService
{
    private readonly IProcessRunner _processRunner;
    private readonly ILogger<MicrosoftUpdateCatalogDriverService> _logger;

    public MicrosoftUpdateCatalogDriverService(IProcessRunner processRunner, ILogger<MicrosoftUpdateCatalogDriverService> logger)
    {
        _processRunner = processRunner;
        _logger = logger;
    }

    public async Task<MicrosoftUpdateCatalogDriverResult> DownloadAsync(
        string destinationDirectory,
        CancellationToken cancellationToken = default,
        IProgress<double>? progress = null)
    {
        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            throw new ArgumentException("Destination directory is required.", nameof(destinationDirectory));
        }

        _logger.LogInformation("Starting Microsoft Update Catalog driver download. DestinationDirectory={DestinationDirectory}", destinationDirectory);
        Directory.CreateDirectory(destinationDirectory);
        progress?.Report(5d);

        string script = BuildScript(destinationDirectory);
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        string args = $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}";
        progress?.Report(15d);

        ProcessExecutionResult execution = await _processRunner
            .RunAsync("powershell.exe", args, destinationDirectory, cancellationToken)
            .ConfigureAwait(false);

        if (!execution.IsSuccess)
        {
            _logger.LogError("Microsoft Update Catalog driver download failed. ExitCode={ExitCode}, StdErr={StdErr}",
                execution.ExitCode,
                execution.StandardError);
            throw new InvalidOperationException(
                "Microsoft Update Catalog download failed." + Environment.NewLine +
                $"ExitCode: {execution.ExitCode}" + Environment.NewLine +
                execution.StandardError);
        }
        progress?.Report(90d);

        int cabCount = Directory
            .EnumerateFiles(destinationDirectory, "*.cab", SearchOption.AllDirectories)
            .Count();
        int infCount = Directory
            .EnumerateFiles(destinationDirectory, "*.inf", SearchOption.AllDirectories)
            .Count();

        _logger.LogInformation("Microsoft Update Catalog driver download completed. DestinationDirectory={DestinationDirectory}, CabCount={CabCount}, InfCount={InfCount}",
            destinationDirectory,
            cabCount,
            infCount);
        progress?.Report(100d);

        return new MicrosoftUpdateCatalogDriverResult
        {
            DestinationDirectory = destinationDirectory,
            CabCount = cabCount,
            InfCount = infCount,
            Message = cabCount > 0
                ? $"Microsoft Update Catalog payload downloaded: {cabCount} CAB files."
                : infCount > 0
                    ? $"Microsoft Update Catalog payload downloaded and already contains {infCount} INF files."
                    : "Microsoft Update Catalog completed, but no CAB or INF files were found."
        };
    }

    public async Task<MicrosoftUpdateCatalogDriverResult> ExpandAsync(
        string sourceDirectory,
        string destinationDirectory,
        CancellationToken cancellationToken = default,
        IProgress<double>? progress = null)
    {
        if (string.IsNullOrWhiteSpace(sourceDirectory))
        {
            throw new ArgumentException("Source directory is required.", nameof(sourceDirectory));
        }

        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            throw new ArgumentException("Destination directory is required.", nameof(destinationDirectory));
        }

        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException($"Microsoft Update Catalog source directory '{sourceDirectory}' was not found.");
        }

        progress?.Report(5d);
        Directory.CreateDirectory(destinationDirectory);

        string[] cabFiles = Directory
            .EnumerateFiles(sourceDirectory, "*.cab", SearchOption.AllDirectories)
            .ToArray();

        if (cabFiles.Length == 0)
        {
            int existingInfCount = Directory
                .EnumerateFiles(sourceDirectory, "*.inf", SearchOption.AllDirectories)
                .Count();
            progress?.Report(100d);

            return new MicrosoftUpdateCatalogDriverResult
            {
                DestinationDirectory = existingInfCount > 0 ? sourceDirectory : destinationDirectory,
                CabCount = 0,
                InfCount = existingInfCount,
                Message = existingInfCount > 0
                    ? $"Microsoft Update Catalog payload is already expanded: {existingInfCount} INF files."
                    : "Microsoft Update Catalog expand completed, but no CAB or INF files were found."
            };
        }

        for (int index = 0; index < cabFiles.Length; index++)
        {
            string cabPath = cabFiles[index];
            string cabDestination = Path.Combine(
                destinationDirectory,
                SanitizePathSegment(Path.GetFileNameWithoutExtension(cabPath)));
            Directory.CreateDirectory(cabDestination);

            ProcessExecutionResult execution = await _processRunner
                .RunAsync(
                    "expand.exe",
                    [
                        cabPath,
                        "-F:*",
                        cabDestination
                    ],
                    destinationDirectory,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!execution.IsSuccess)
            {
                throw new InvalidOperationException(
                    "Microsoft Update Catalog expand failed." + Environment.NewLine +
                    $"ExitCode: {execution.ExitCode}" + Environment.NewLine +
                    execution.StandardOutput + Environment.NewLine +
                    execution.StandardError);
            }

            double percent = 10d + (double)(index + 1) / cabFiles.Length * 85d;
            progress?.Report(percent);
        }

        int infCount = Directory
            .EnumerateFiles(destinationDirectory, "*.inf", SearchOption.AllDirectories)
            .Count();
        progress?.Report(100d);

        return new MicrosoftUpdateCatalogDriverResult
        {
            DestinationDirectory = destinationDirectory,
            CabCount = cabFiles.Length,
            InfCount = infCount,
            Message = infCount > 0
                ? $"Microsoft Update Catalog payload expanded: {infCount} INF files from {cabFiles.Length} CAB files."
                : $"Microsoft Update Catalog payload expanded from {cabFiles.Length} CAB files, but no INF files were found."
        };
    }

    private static string BuildScript(string destinationDirectory)
    {
        string escapedDestination = destinationDirectory.Replace("'", "''");

        string template = @"
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$destination = '__DESTINATION__'

function Ensure-Module([string]$Name) {
    if (Get-Module -ListAvailable -Name $Name) {
        return
    }

    if (-not (Get-PackageProvider -Name NuGet -ListAvailable -ErrorAction SilentlyContinue)) {
        Install-PackageProvider -Name NuGet -Scope AllUsers -Force -ErrorAction Stop
    }

    try {
        Set-PSRepository -Name PSGallery -InstallationPolicy Trusted -ErrorAction Stop
    } catch {
        Write-Warning $_.Exception.Message
    }

    Install-Module -Name $Name -Scope AllUsers -Force -AllowClobber -ErrorAction Stop
}

Ensure-Module -Name 'OSD'
Import-Module OSD -Force -ErrorAction Stop

Save-MsUpCatDriver -DestinationDirectory $destination -ErrorAction Stop
";

        return template.Replace("__DESTINATION__", escapedDestination, StringComparison.Ordinal);
    }

    private static string SanitizePathSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "package";
        }

        char[] invalid = Path.GetInvalidFileNameChars();
        string sanitized = new(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return sanitized.Trim().TrimEnd('.');
    }
}
