using System.IO;
using System.Text;
using System.Text.Json;
using Foundry.Deploy.Models;
using Foundry.Deploy.Services.System;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.Services.Autopilot;

public sealed class AutopilotService : IAutopilotService
{
    private const string GroupTag = "Foundry";
    private const string MarkerBegin = "REM >>> FOUNDRY AUTOPILOT BEGIN";
    private const string MarkerEnd = "REM <<< FOUNDRY AUTOPILOT END";

    private readonly IProcessRunner _processRunner;
    private readonly ILogger<AutopilotService> _logger;

    public AutopilotService(IProcessRunner processRunner, ILogger<AutopilotService> logger)
    {
        _processRunner = processRunner;
        _logger = logger;
    }

    public async Task<AutopilotExecutionResult> ExecuteFullWorkflowAsync(
        string cacheRootPath,
        string windowsPartitionRoot,
        HardwareProfile hardwareProfile,
        OperatingSystemCatalogItem operatingSystem,
        bool allowDeferredCompletion,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting full Autopilot workflow. CacheRootPath={CacheRootPath}, WindowsPartitionRoot={WindowsPartitionRoot}, AllowDeferredCompletion={AllowDeferredCompletion}",
            cacheRootPath,
            windowsPartitionRoot,
            allowDeferredCompletion);

        string autopilotRoot = Path.Combine(cacheRootPath, "Autopilot");
        Directory.CreateDirectory(autopilotRoot);

        string manifestPath = Path.Combine(autopilotRoot, "autopilot-workflow.json");
        string onlineScriptPath = Path.Combine(autopilotRoot, "Invoke-FoundryAutopilot.ps1");
        string onlineTranscriptPath = Path.Combine(autopilotRoot, "autopilot-transcript.log");
        string hashCsvPath = Path.Combine(autopilotRoot, "autopilot-device.csv");

        string targetAutopilotRoot = Path.Combine(windowsPartitionRoot, "Windows", "Temp", "Foundry", "Autopilot");
        Directory.CreateDirectory(targetAutopilotRoot);
        string targetMarkerPath = Path.Combine(targetAutopilotRoot, "autopilot-status.json");
        string targetCsvPath = Path.Combine(targetAutopilotRoot, "autopilot-device.csv");
        string deferredScriptPath = Path.Combine(targetAutopilotRoot, "Invoke-FoundryAutopilot-Deferred.ps1");
        string setupCompletePath = Path.Combine(windowsPartitionRoot, "Windows", "Setup", "Scripts", "SetupComplete.cmd");

        string onlineScript = BuildOnlineScript(hashCsvPath);
        await File.WriteAllTextAsync(onlineScriptPath, onlineScript, cancellationToken).ConfigureAwait(false);

        ProcessExecutionResult onlineExecution = await ExecuteScriptAsync(onlineScriptPath, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(onlineTranscriptPath, BuildTranscript(onlineExecution), cancellationToken).ConfigureAwait(false);

        if (File.Exists(hashCsvPath))
        {
            File.Copy(hashCsvPath, targetCsvPath, overwrite: true);
        }

        if (onlineExecution.IsSuccess)
        {
            _logger.LogInformation("Autopilot online registration succeeded in WinPE.");
            string successJson = BuildStatusJson(
                state: "completed-online",
                message: "Autopilot online registration completed during WinPE deployment.",
                allowDeferredCompletion,
                onlineRegistrationSucceeded: true,
                deferredPrepared: false,
                hardwareProfile,
                operatingSystem,
                onlineScriptPath,
                onlineTranscriptPath,
                hashCsvPath,
                deferredScriptPath: null,
                setupCompletePath: null);

            await File.WriteAllTextAsync(manifestPath, successJson, cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(targetMarkerPath, successJson, cancellationToken).ConfigureAwait(false);

            return new AutopilotExecutionResult
            {
                IsSuccess = true,
                OnlineRegistrationSucceeded = true,
                DeferredCompletionPrepared = false,
                Message = "Autopilot online registration completed.",
                WorkflowManifestPath = manifestPath,
                WorkflowScriptPath = onlineScriptPath,
                TranscriptPath = onlineTranscriptPath,
                DeferredScriptPath = null,
                SetupCompleteHookPath = null
            };
        }

        if (!allowDeferredCompletion)
        {
            _logger.LogWarning("Autopilot online registration failed and deferred completion is disabled. ExitCode={ExitCode}", onlineExecution.ExitCode);
            string failureJson = BuildStatusJson(
                state: "failed-online",
                message: "Autopilot online registration failed during WinPE deployment.",
                allowDeferredCompletion,
                onlineRegistrationSucceeded: false,
                deferredPrepared: false,
                hardwareProfile,
                operatingSystem,
                onlineScriptPath,
                onlineTranscriptPath,
                hashCsvPath,
                deferredScriptPath: null,
                setupCompletePath: null);

            await File.WriteAllTextAsync(manifestPath, failureJson, cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(targetMarkerPath, failureJson, cancellationToken).ConfigureAwait(false);

            return new AutopilotExecutionResult
            {
                IsSuccess = false,
                OnlineRegistrationSucceeded = false,
                DeferredCompletionPrepared = false,
                Message = "Autopilot online registration failed. Deferred completion is disabled.",
                WorkflowManifestPath = manifestPath,
                WorkflowScriptPath = onlineScriptPath,
                TranscriptPath = onlineTranscriptPath,
                DeferredScriptPath = null,
                SetupCompleteHookPath = null
            };
        }

        string deferredScript = BuildDeferredScript();
        await File.WriteAllTextAsync(deferredScriptPath, deferredScript, cancellationToken).ConfigureAwait(false);
        EnsureSetupCompleteHook(setupCompletePath);
        _logger.LogWarning("Autopilot online registration failed; deferred completion has been prepared. DeferredScriptPath={DeferredScriptPath}, SetupCompletePath={SetupCompletePath}",
            deferredScriptPath,
            setupCompletePath);

        string deferredJson = BuildStatusJson(
            state: "deferred",
            message: "Autopilot online registration failed in WinPE. Deferred completion has been prepared for first boot.",
            allowDeferredCompletion,
            onlineRegistrationSucceeded: false,
            deferredPrepared: true,
            hardwareProfile,
            operatingSystem,
            onlineScriptPath,
            onlineTranscriptPath,
            hashCsvPath,
            deferredScriptPath,
            setupCompletePath);

        await File.WriteAllTextAsync(manifestPath, deferredJson, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(targetMarkerPath, deferredJson, cancellationToken).ConfigureAwait(false);

        return new AutopilotExecutionResult
        {
            IsSuccess = true,
            OnlineRegistrationSucceeded = false,
            DeferredCompletionPrepared = true,
            Message = "Autopilot online registration failed in WinPE; deferred completion has been prepared for first boot.",
            WorkflowManifestPath = manifestPath,
            WorkflowScriptPath = onlineScriptPath,
            TranscriptPath = onlineTranscriptPath,
            DeferredScriptPath = deferredScriptPath,
            SetupCompleteHookPath = setupCompletePath
        };
    }

    private async Task<ProcessExecutionResult> ExecuteScriptAsync(string scriptPath, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Executing Autopilot script. ScriptPath={ScriptPath}", scriptPath);
        string args = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"";
        return await _processRunner
            .RunAsync("powershell.exe", args, Path.GetDirectoryName(scriptPath)!, cancellationToken)
            .ConfigureAwait(false);
    }

    private static string BuildOnlineScript(string hashCsvPath)
    {
        string template = @"
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$groupTag = '__GROUPTAG__'
$csvPath = '__CSVPATH__'

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

        return template
            .Replace("__GROUPTAG__", EscapeForSingleQuote(GroupTag), StringComparison.Ordinal)
            .Replace("__CSVPATH__", EscapeForSingleQuote(hashCsvPath), StringComparison.Ordinal);
    }

    private static string BuildDeferredScript()
    {
        string template = @"
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$groupTag = '__GROUPTAG__'
$statusPath = 'C:\Windows\Temp\Foundry\Autopilot\autopilot-status.json'
$csvPath = 'C:\Windows\Temp\Foundry\Autopilot\autopilot-device.csv'
$maxAttempts = 10
$sleepSeconds = 30

function Write-Status([string]$state, [string]$message, [bool]$onlineSucceeded) {{
    $payload = [pscustomobject]@{{
        updatedAtUtc = [DateTime]::UtcNow.ToString('o')
        state = $state
        message = $message
        onlineRegistrationSucceeded = $onlineSucceeded
    }}

    $payload | ConvertTo-Json -Depth 6 | Set-Content -Path $statusPath -Encoding UTF8
}}

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

try {{
    Ensure-Command -Name 'Get-WindowsAutopilotInfo'

    if (-not (Test-Path -Path $csvPath)) {{
        Get-WindowsAutopilotInfo -OutputFile $csvPath -ErrorAction Stop | Out-Null
    }}

    for ($attempt = 1; $attempt -le $maxAttempts; $attempt++) {{
        try {{
            Get-WindowsAutopilotInfo -Online -Assign -GroupTag $groupTag -ErrorAction Stop | Out-Null
            Write-Status -state 'completed-online' -message 'Deferred Autopilot completion succeeded.' -onlineSucceeded $true
            exit 0
        }} catch {{
            if ($attempt -eq $maxAttempts) {{
                throw
            }}

            Start-Sleep -Seconds $sleepSeconds
        }}
    }}
}} catch {{
    $errorMessage = $_.Exception.Message
    Write-Status -state 'deferred-failed' -message $errorMessage -onlineSucceeded $false
    throw
}}
";

        return template.Replace("__GROUPTAG__", EscapeForSingleQuote(GroupTag), StringComparison.Ordinal);
    }

    private static string BuildStatusJson(
        string state,
        string message,
        bool allowDeferredCompletion,
        bool onlineRegistrationSucceeded,
        bool deferredPrepared,
        HardwareProfile hardwareProfile,
        OperatingSystemCatalogItem operatingSystem,
        string onlineScriptPath,
        string onlineTranscriptPath,
        string hashCsvPath,
        string? deferredScriptPath,
        string? setupCompletePath)
    {
        return JsonSerializer.Serialize(new
        {
            createdAtUtc = DateTimeOffset.UtcNow,
            mode = "full",
            telemetry = "disabled",
            state,
            message,
            allowDeferredCompletion,
            onlineRegistrationSucceeded,
            deferredPrepared,
            hardware = hardwareProfile,
            operatingSystem = new
            {
                operatingSystem.WindowsRelease,
                operatingSystem.ReleaseId,
                operatingSystem.Architecture,
                operatingSystem.LanguageCode,
                operatingSystem.Edition
            },
            artifacts = new
            {
                onlineScriptPath,
                onlineTranscriptPath,
                hashCsvPath,
                deferredScriptPath,
                setupCompletePath
            }
        }, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    private static void EnsureSetupCompleteHook(string setupCompletePath)
    {
        string directory = Path.GetDirectoryName(setupCompletePath)
            ?? throw new InvalidOperationException("Unable to resolve SetupComplete directory.");
        Directory.CreateDirectory(directory);

        string snippet =
            $"{MarkerBegin}{Environment.NewLine}" +
            "if exist \"C:\\Windows\\Temp\\Foundry\\Autopilot\\Invoke-FoundryAutopilot-Deferred.ps1\" (" + Environment.NewLine +
            "  powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"C:\\Windows\\Temp\\Foundry\\Autopilot\\Invoke-FoundryAutopilot-Deferred.ps1\" >> \"C:\\Windows\\Temp\\Foundry\\Autopilot\\deferred-autopilot-transcript.log\" 2>&1" + Environment.NewLine +
            ")" + Environment.NewLine +
            $"{MarkerEnd}{Environment.NewLine}";

        if (!File.Exists(setupCompletePath))
        {
            File.WriteAllText(setupCompletePath, "@echo off" + Environment.NewLine + snippet);
            return;
        }

        string existing = File.ReadAllText(setupCompletePath);
        if (existing.Contains(MarkerBegin, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string separator = existing.EndsWith(Environment.NewLine, StringComparison.Ordinal) ? string.Empty : Environment.NewLine;
        File.WriteAllText(setupCompletePath, existing + separator + snippet);
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
