using System.IO;
using System.Text;
using System.Text.Json;
using Foundry.Deploy.Models;
using Foundry.Deploy.Services.System;

namespace Foundry.Deploy.Services.Autopilot;

public sealed class AutopilotService : IAutopilotService
{
    private readonly IProcessRunner _processRunner;

    public AutopilotService(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public async Task<AutopilotExecutionResult> ExecuteFullWorkflowAsync(
        string cacheRootPath,
        string windowsPartitionRoot,
        HardwareProfile hardwareProfile,
        OperatingSystemCatalogItem operatingSystem,
        CancellationToken cancellationToken = default)
    {
        string autopilotRoot = Path.Combine(cacheRootPath, "Autopilot");
        Directory.CreateDirectory(autopilotRoot);

        string manifestPath = Path.Combine(autopilotRoot, "autopilot-workflow.json");
        string scriptPath = Path.Combine(autopilotRoot, "Invoke-FoundryAutopilot.ps1");
        string transcriptPath = Path.Combine(autopilotRoot, "autopilot-transcript.log");
        string hashCsvPath = Path.Combine(autopilotRoot, "autopilot-device.csv");

        string script = $@"
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$groupTag = 'Foundry'
$csvPath = '{EscapeForSingleQuote(hashCsvPath)}'

function Ensure-Command([string]$Name) {{
    if (Get-Command -Name $Name -ErrorAction SilentlyContinue) {{
        return
    }}

    if (-not (Get-PackageProvider -Name NuGet -ListAvailable -ErrorAction SilentlyContinue)) {{
        Install-PackageProvider -Name NuGet -Scope AllUsers -Force -ErrorAction Stop
    }}

    try {{
        Set-PSRepository -Name PSGallery -InstallationPolicy Trusted -ErrorAction Stop
    }} catch {{
        Write-Warning $_.Exception.Message
    }}

    Install-Script -Name Get-WindowsAutopilotInfo -Scope AllUsers -Force -ErrorAction Stop
    if (-not (Get-Command -Name $Name -ErrorAction SilentlyContinue)) {{
        throw ""Command '$Name' is still unavailable after installation.""
    }}
}}

Write-Host '[Foundry.Autopilot] Ensuring Get-WindowsAutopilotInfo command is available...'
Ensure-Command -Name 'Get-WindowsAutopilotInfo'

Write-Host '[Foundry.Autopilot] Exporting hardware hash to local cache...'
Get-WindowsAutopilotInfo -OutputFile $csvPath -ErrorAction Stop | Out-Null

Write-Host '[Foundry.Autopilot] Registering device online (assign + group tag)...'
Get-WindowsAutopilotInfo -Online -Assign -GroupTag $groupTag -ErrorAction Stop | Out-Null
";

        await File.WriteAllTextAsync(scriptPath, script, cancellationToken).ConfigureAwait(false);

        ProcessExecutionResult execution = await ExecuteScriptAsync(scriptPath, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(transcriptPath, BuildTranscript(execution), cancellationToken).ConfigureAwait(false);

        string markerDirectory = Path.Combine(windowsPartitionRoot, "ProgramData", "Foundry", "Autopilot");
        Directory.CreateDirectory(markerDirectory);
        string markerPath = Path.Combine(markerDirectory, "autopilot-status.json");

        string json = JsonSerializer.Serialize(new
        {
            createdAtUtc = DateTimeOffset.UtcNow,
            mode = "full",
            telemetry = "disabled",
            hardware = hardwareProfile,
            operatingSystem = new
            {
                operatingSystem.WindowsRelease,
                operatingSystem.ReleaseId,
                operatingSystem.Architecture,
                operatingSystem.LanguageCode,
                operatingSystem.Edition
            },
            scriptPath,
            transcriptPath,
            hashCsvPath,
            onlineRegistrationSucceeded = execution.IsSuccess
        }, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        await File.WriteAllTextAsync(manifestPath, json, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(markerPath, json, cancellationToken).ConfigureAwait(false);

        if (!execution.IsSuccess)
        {
            return new AutopilotExecutionResult
            {
                IsSuccess = false,
                Message = "Autopilot online registration failed. See transcript for details.",
                WorkflowManifestPath = manifestPath,
                WorkflowScriptPath = scriptPath,
                TranscriptPath = transcriptPath
            };
        }

        return new AutopilotExecutionResult
        {
            IsSuccess = true,
            Message = "Autopilot online registration completed.",
            WorkflowManifestPath = manifestPath,
            WorkflowScriptPath = scriptPath,
            TranscriptPath = transcriptPath
        };
    }

    private async Task<ProcessExecutionResult> ExecuteScriptAsync(string scriptPath, CancellationToken cancellationToken)
    {
        string args = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"";
        return await _processRunner
            .RunAsync("powershell.exe", args, Path.GetDirectoryName(scriptPath)!, cancellationToken)
            .ConfigureAwait(false);
    }

    private static string BuildTranscript(ProcessExecutionResult execution)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"[{DateTimeOffset.UtcNow:O}] ExitCode={execution.ExitCode}");
        builder.AppendLine("[STDOUT]");
        builder.AppendLine(execution.StandardOutput);
        builder.AppendLine("[STDERR]");
        builder.AppendLine(execution.StandardError);
        return builder.ToString();
    }

    private static string EscapeForSingleQuote(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }
}
