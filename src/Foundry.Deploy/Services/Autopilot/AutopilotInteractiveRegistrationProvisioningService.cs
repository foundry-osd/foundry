using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Foundry.Deploy.Services.Deployment;

namespace Foundry.Deploy.Services.Autopilot;

/// <summary>
/// Stages the interactive Autopilot registration assistant into the retained Foundry runtime path.
/// </summary>
public sealed class AutopilotInteractiveRegistrationProvisioningService : IAutopilotInteractiveRegistrationProvisioningService
{
    private const string ScriptFileName = "Start-FoundryAutopilotRegistration.ps1";
    private const string LauncherFileName = "Start-FoundryAutopilotRegistration.cmd";
    private const string OobeLauncherFileName = "Start-FoundryAutopilotRegistrationOobe.cmd";
    private const string OobeWaiterFileName = "Wait-FoundryAutopilotRegistrationOobe.ps1";
    private const string OobeCommandFileName = "OOBE.cmd";
    private const string ConfigFileName = "config.json";
    private const string SetupCompleteMarkerKey = "FOUNDRY AUTOPILOT REGISTRATION";
    private const string ScriptResourceName = "Foundry.Deploy.AutopilotRegistration.Start-FoundryAutopilotRegistration.ps1";
    private const string RuntimeRegistrationRoot = "%SystemRoot%\\Temp\\Foundry\\AutopilotRegistration";
    private const string RuntimeLogRoot = "%SystemRoot%\\Temp\\Foundry\\Logs\\AutopilotRegistration";
    private const string RuntimeStateRoot = "%SystemRoot%\\Temp\\Foundry\\AutopilotRegistration\\State";
    private const string FoundryBootstrapClientId = "83eb3a92-030d-49b7-881b-32a1eb3e110a";
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private readonly ISetupCompleteScriptService _setupCompleteScriptService;

    public AutopilotInteractiveRegistrationProvisioningService(ISetupCompleteScriptService setupCompleteScriptService)
    {
        _setupCompleteScriptService = setupCompleteScriptService;
    }

    /// <inheritdoc />
    public AutopilotInteractiveRegistrationProvisioningResult Provision(string targetWindowsPartitionRoot)
    {
        if (string.IsNullOrWhiteSpace(targetWindowsPartitionRoot))
        {
            throw new ArgumentException("Target Windows partition root is required.", nameof(targetWindowsPartitionRoot));
        }

        string registrationRoot = GetRegistrationRoot(targetWindowsPartitionRoot);
        string stateRoot = Path.Combine(registrationRoot, "State");
        string logRoot = GetLogRoot(targetWindowsPartitionRoot);
        string scriptPath = Path.Combine(registrationRoot, ScriptFileName);
        string launcherPath = Path.Combine(registrationRoot, LauncherFileName);
        string oobeLauncherPath = Path.Combine(registrationRoot, OobeLauncherFileName);
        string oobeWaiterPath = Path.Combine(registrationRoot, OobeWaiterFileName);
        string oobeCommandPath = GetOobeCommandPath(targetWindowsPartitionRoot);
        string configPath = Path.Combine(registrationRoot, ConfigFileName);
        string setupCompletePath = GetSetupCompletePath(targetWindowsPartitionRoot);

        Directory.CreateDirectory(registrationRoot);
        Directory.CreateDirectory(stateRoot);
        Directory.CreateDirectory(logRoot);

        StageScript(scriptPath);
        File.WriteAllText(launcherPath, BuildLauncher(), Encoding.ASCII);
        File.WriteAllText(oobeLauncherPath, BuildOobeLauncher(), Encoding.ASCII);
        File.WriteAllText(oobeWaiterPath, BuildOobeWaiter(), Encoding.ASCII);
        File.WriteAllText(configPath, BuildConfig(), Utf8NoBom);

        _setupCompleteScriptService.RemoveBlock(setupCompletePath, SetupCompleteMarkerKey);
        _setupCompleteScriptService.RemoveBlock(oobeCommandPath, SetupCompleteMarkerKey);
        _setupCompleteScriptService.EnsureBlock(
            oobeCommandPath,
            SetupCompleteMarkerKey,
            BuildOobeCommandLauncher());

        return new AutopilotInteractiveRegistrationProvisioningResult
        {
            RegistrationRootPath = registrationRoot,
            ScriptPath = scriptPath,
            LauncherPath = launcherPath,
            OobeLauncherPath = oobeLauncherPath,
            OobeWaiterPath = oobeWaiterPath,
            OobeCommandPath = oobeCommandPath,
            ConfigPath = configPath,
            StateRootPath = stateRoot,
            LogRootPath = logRoot
        };
    }

    private static string GetRegistrationRoot(string targetWindowsPartitionRoot)
    {
        return Path.Combine(targetWindowsPartitionRoot, "Windows", "Temp", "Foundry", "AutopilotRegistration");
    }

    private static string GetLogRoot(string targetWindowsPartitionRoot)
    {
        return Path.Combine(targetWindowsPartitionRoot, "Windows", "Temp", "Foundry", "Logs", "AutopilotRegistration");
    }

    private static string GetSetupCompletePath(string targetWindowsPartitionRoot)
    {
        return Path.Combine(targetWindowsPartitionRoot, "Windows", "Setup", "Scripts", "SetupComplete.cmd");
    }

    private static string GetOobeCommandPath(string targetWindowsPartitionRoot)
    {
        return Path.Combine(targetWindowsPartitionRoot, "Windows", "Setup", "Scripts", OobeCommandFileName);
    }

    private static void StageScript(string scriptPath)
    {
        Assembly assembly = typeof(AutopilotInteractiveRegistrationProvisioningService).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(ScriptResourceName);
        if (stream is null)
        {
            throw new InvalidOperationException($"Embedded Autopilot registration assistant resource '{ScriptResourceName}' was not found.");
        }

        using FileStream destination = File.Create(scriptPath);
        stream.CopyTo(destination);
    }

    private static string BuildLauncher()
    {
        return string.Join(
            Environment.NewLine,
            [
                "@echo off",
                "setlocal",
                $"set \"FOUNDRY_AUTOPILOT_LOG_ROOT={RuntimeLogRoot}\"",
                "mkdir \"%FOUNDRY_AUTOPILOT_LOG_ROOT%\" >nul 2>&1",
                "echo [%date% %time%] Starting Foundry Autopilot registration assistant.>>\"%FOUNDRY_AUTOPILOT_LOG_ROOT%\\launcher.log\"",
                $"powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File \"{RuntimeRegistrationRoot}\\{ScriptFileName}\" -ConfigPath \"{RuntimeRegistrationRoot}\\{ConfigFileName}\"",
                "set \"FOUNDRY_AUTOPILOT_EXIT=%ERRORLEVEL%\"",
                "echo [%date% %time%] Foundry Autopilot registration assistant exited with %FOUNDRY_AUTOPILOT_EXIT%.>>\"%FOUNDRY_AUTOPILOT_LOG_ROOT%\\launcher.log\"",
                "exit /b %FOUNDRY_AUTOPILOT_EXIT%",
                string.Empty
            ]);
    }

    private static string BuildOobeCommandLauncher()
    {
        return string.Join(
            Environment.NewLine,
            [
                $"mkdir \"{RuntimeLogRoot}\" >nul 2>&1",
                $"echo [%date% %time%] Calling Foundry Autopilot OOBE registration launcher.>>\"{RuntimeLogRoot}\\OOBE.log\"",
                $"call \"{RuntimeRegistrationRoot}\\{OobeLauncherFileName}\"",
                "set \"FOUNDRY_AUTOPILOT_OOBE_EXIT=%ERRORLEVEL%\"",
                $"echo [%date% %time%] Foundry Autopilot OOBE registration launcher exited with %FOUNDRY_AUTOPILOT_OOBE_EXIT%.>>\"{RuntimeLogRoot}\\OOBE.log\""
            ]);
    }

    private static string BuildOobeLauncher()
    {
        return string.Join(
            Environment.NewLine,
            [
                "@echo off",
                "setlocal EnableExtensions",
                $"set \"FOUNDRY_AUTOPILOT_REGISTRATION_ROOT={RuntimeRegistrationRoot}\"",
                $"set \"FOUNDRY_AUTOPILOT_LOG_ROOT={RuntimeLogRoot}\"",
                "set \"FOUNDRY_AUTOPILOT_SCRIPT=%FOUNDRY_AUTOPILOT_REGISTRATION_ROOT%\\Start-FoundryAutopilotRegistration.ps1\"",
                "set \"FOUNDRY_AUTOPILOT_CONFIG=%FOUNDRY_AUTOPILOT_REGISTRATION_ROOT%\\config.json\"",
                "set \"FOUNDRY_AUTOPILOT_WAITER=%FOUNDRY_AUTOPILOT_REGISTRATION_ROOT%\\Wait-FoundryAutopilotRegistrationOobe.ps1\"",
                "set \"FOUNDRY_AUTOPILOT_LOG=%FOUNDRY_AUTOPILOT_LOG_ROOT%\\oobe-launcher.log\"",
                "set \"FOUNDRY_AUTOPILOT_PS=%SystemRoot%\\System32\\WindowsPowerShell\\v1.0\\powershell.exe\"",
                "mkdir \"%FOUNDRY_AUTOPILOT_LOG_ROOT%\" >nul 2>&1",
                "echo [%date% %time%] Starting Foundry Autopilot OOBE registration launcher.>>\"%FOUNDRY_AUTOPILOT_LOG%\"",
                "if not exist \"%FOUNDRY_AUTOPILOT_WAITER%\" (",
                "    echo [%date% %time%] OOBE waiter was not found: %FOUNDRY_AUTOPILOT_WAITER%.>>\"%FOUNDRY_AUTOPILOT_LOG%\"",
                "    exit /b 0",
                ")",
                "start \"\" \"%FOUNDRY_AUTOPILOT_PS%\" -NoLogo -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"%FOUNDRY_AUTOPILOT_WAITER%\"",
                "echo [%date% %time%] Foundry Autopilot OOBE waiter started.>>\"%FOUNDRY_AUTOPILOT_LOG%\"",
                "exit /b 0",
                string.Empty
            ]);
    }

    private static string BuildOobeWaiter()
    {
        return string.Join(
            Environment.NewLine,
            [
                "$ErrorActionPreference = 'Stop'",
                "$registrationRoot = Join-Path $env:SystemRoot 'Temp\\Foundry\\AutopilotRegistration'",
                "$logRoot = Join-Path $env:SystemRoot 'Temp\\Foundry\\Logs\\AutopilotRegistration'",
                "$statePath = Join-Path $registrationRoot 'State\\registration-result.json'",
                "$registrationScriptPath = Join-Path $registrationRoot 'Start-FoundryAutopilotRegistration.ps1'",
                "$configPath = Join-Path $registrationRoot 'config.json'",
                "$powershellPath = Join-Path $env:SystemRoot 'System32\\WindowsPowerShell\\v1.0\\powershell.exe'",
                "$waitLogPath = Join-Path $logRoot 'oobe-waiter.log'",
                "$oobeProcessNames = @('CloudExperienceHost', 'CloudExperienceHostBroker', 'UserOOBEBroker')",
                "$timeout = [DateTimeOffset]::UtcNow.AddMinutes(20)",
                "$stableSeconds = 5",
                "New-Item -Path $logRoot -ItemType Directory -Force | Out-Null",
                "function Write-FoundryOobeWaiterLog {",
                "    param([Parameter(Mandatory = $true)][string]$Message)",
                "    $timestamp = [DateTimeOffset]::Now.ToString('o')",
                "    Add-Content -LiteralPath $waitLogPath -Value \"[$timestamp] $Message\"",
                "}",
                "try {",
                "function Test-FoundryRegistrationCompleted {",
                "    if (-not (Test-Path -LiteralPath $statePath)) {",
                "        return $false",
                "    }",
                "    try {",
                "        $result = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json",
                "        return $result.status -eq 'completed'",
                "    }",
                "    catch {",
                "        Write-FoundryOobeWaiterLog -Message \"Failed to read existing registration result. $($_.Exception.Message)\"",
                "        return $false",
                "    }",
                "}",
                "function Test-FoundryOobeProcessReady {",
                "    foreach ($processName in $oobeProcessNames) {",
                "        if (Get-Process -Name $processName -ErrorAction SilentlyContinue) {",
                "            return $true",
                "        }",
                "    }",
                "    return $false",
                "}",
                "Write-FoundryOobeWaiterLog -Message 'Waiting for OOBE process readiness.'",
                "while ([DateTimeOffset]::UtcNow -lt $timeout) {",
                "    if (Test-FoundryRegistrationCompleted) {",
                "        Write-FoundryOobeWaiterLog -Message 'Autopilot registration is already completed.'",
                "        exit 0",
                "    }",
                "    if (Test-FoundryOobeProcessReady) {",
                "        Write-FoundryOobeWaiterLog -Message \"OOBE process detected. Waiting $stableSeconds seconds before launching assistant.\"",
                "        Start-Sleep -Seconds $stableSeconds",
                "        break",
                "    }",
                "    Start-Sleep -Seconds 2",
                "}",
                "if (-not (Test-FoundryOobeProcessReady)) {",
                "    Write-FoundryOobeWaiterLog -Message 'Timed out while waiting for OOBE process readiness.'",
                "    exit 0",
                "}",
                "if (-not (Test-Path -LiteralPath $registrationScriptPath)) {",
                "    Write-FoundryOobeWaiterLog -Message \"Registration script was not found: $registrationScriptPath\"",
                "    exit 0",
                "}",
                "Write-FoundryOobeWaiterLog -Message 'Launching Foundry Autopilot registration assistant.'",
                "$arguments = @(",
                "    '-NoLogo',",
                "    '-NoProfile',",
                "    '-ExecutionPolicy',",
                "    'Bypass',",
                "    '-STA',",
                "    '-WindowStyle',",
                "    'Hidden',",
                "    '-File',",
                "    $registrationScriptPath,",
                "    '-ConfigPath',",
                "    $configPath",
                ")",
                "$process = Start-Process -FilePath $powershellPath -ArgumentList $arguments -PassThru",
                "Write-FoundryOobeWaiterLog -Message \"Registration assistant process started with PID $($process.Id).\"",
                "}",
                "catch {",
                "    try {",
                "        Write-FoundryOobeWaiterLog -Message \"OOBE waiter failed. $($_.Exception.Message)\"",
                "    }",
                "    catch {",
                "    }",
                "    exit 0",
                "}",
                string.Empty
            ]);
    }

    private static string BuildConfig()
    {
        string json = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            provisioningMode = "interactiveHardwareHashUpload",
            tenant = "common",
            clientId = FoundryBootstrapClientId,
            graphBaseUri = "https://graph.microsoft.com/v1.0",
            scopes = new[]
            {
                "DeviceManagementServiceConfig.ReadWrite.All"
            },
            registrationRootPath = RuntimeRegistrationRoot,
            logRootPath = RuntimeLogRoot,
            stateRootPath = RuntimeStateRoot,
            importPollingTimeoutSeconds = 900,
            importPollingIntervalSeconds = 15
        }, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        return json + Environment.NewLine;
    }
}
