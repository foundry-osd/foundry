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
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            throw new ArgumentException("Destination directory is required.", nameof(destinationDirectory));
        }

        _logger.LogInformation("Starting Microsoft Update Catalog driver download. DestinationDirectory={DestinationDirectory}", destinationDirectory);
        Directory.CreateDirectory(destinationDirectory);

        string script = BuildScript(destinationDirectory);
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        string args = $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}";

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

        int infCount = Directory
            .EnumerateFiles(destinationDirectory, "*.inf", SearchOption.AllDirectories)
            .Count();

        _logger.LogInformation("Microsoft Update Catalog driver download completed. DestinationDirectory={DestinationDirectory}, InfCount={InfCount}",
            destinationDirectory,
            infCount);

        return new MicrosoftUpdateCatalogDriverResult
        {
            DestinationDirectory = destinationDirectory,
            InfCount = infCount,
            Message = infCount > 0
                ? $"Microsoft Update Catalog drivers downloaded: {infCount} INF files."
                : "Microsoft Update Catalog completed, but no INF files were found."
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

$cabFiles = Get-ChildItem -Path $destination -Filter *.cab -File -Recurse -ErrorAction SilentlyContinue
foreach ($cab in $cabFiles) {
    $expandPath = Join-Path $cab.DirectoryName ($cab.BaseName + '_expanded')
    if (-not (Test-Path $expandPath)) {
        New-Item -Path $expandPath -ItemType Directory -Force | Out-Null
    }

    & expand.exe $cab.FullName -F:* $expandPath | Out-Null
}
";

        return template.Replace("__DESTINATION__", escapedDestination, StringComparison.Ordinal);
    }
}
