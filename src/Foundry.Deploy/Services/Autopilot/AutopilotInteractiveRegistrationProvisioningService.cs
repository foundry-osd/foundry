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
    private const string AutomaticLauncherFileName = "Start-FoundryAutopilotRegistrationAutoLaunch.cmd";
    private const string AutomaticLaunchScriptFileName = "Register-FoundryAutopilotRegistrationTask.ps1";
    private const string ConfigFileName = "config.json";
    private const string SetupCompleteMarkerKey = "FOUNDRY AUTOPILOT REGISTRATION";
    private const string ScriptResourceName = "Foundry.Deploy.AutopilotRegistration.Start-FoundryAutopilotRegistration.ps1";
    private const string RuntimeRegistrationRoot = "%SystemRoot%\\Temp\\Foundry\\AutopilotRegistration";
    private const string RuntimeLogRoot = "%SystemRoot%\\Temp\\Foundry\\Logs\\AutopilotRegistration";
    private const string RuntimeStateRoot = "%SystemRoot%\\Temp\\Foundry\\AutopilotRegistration\\State";
    private const string FoundryBootstrapClientId = "83eb3a92-030d-49b7-881b-32a1eb3e110a";
    private const string AutomaticLaunchTaskName = "FoundryAutopilotInteractiveRegistration";
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
        string automaticLauncherPath = Path.Combine(registrationRoot, AutomaticLauncherFileName);
        string automaticLaunchScriptPath = Path.Combine(registrationRoot, AutomaticLaunchScriptFileName);
        string configPath = Path.Combine(registrationRoot, ConfigFileName);
        string setupCompletePath = GetSetupCompletePath(targetWindowsPartitionRoot);

        Directory.CreateDirectory(registrationRoot);
        Directory.CreateDirectory(stateRoot);
        Directory.CreateDirectory(logRoot);

        StageScript(scriptPath);
        File.WriteAllText(launcherPath, BuildLauncher(), Encoding.ASCII);
        File.WriteAllText(automaticLauncherPath, BuildAutomaticLauncher(), Encoding.ASCII);
        File.WriteAllText(automaticLaunchScriptPath, BuildAutomaticLaunchScript(), Encoding.ASCII);
        File.WriteAllText(configPath, BuildConfig(), Utf8NoBom);
        _setupCompleteScriptService.EnsureBlock(
            setupCompletePath,
            SetupCompleteMarkerKey,
            BuildSetupCompleteLauncher());

        return new AutopilotInteractiveRegistrationProvisioningResult
        {
            RegistrationRootPath = registrationRoot,
            ScriptPath = scriptPath,
            LauncherPath = launcherPath,
            AutomaticLauncherPath = automaticLauncherPath,
            AutomaticLaunchScriptPath = automaticLaunchScriptPath,
            ConfigPath = configPath,
            SetupCompletePath = setupCompletePath,
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

    private static string BuildSetupCompleteLauncher()
    {
        return string.Join(
            Environment.NewLine,
            [
                $"mkdir \"{RuntimeLogRoot}\" >nul 2>&1",
                $"echo [%date% %time%] Registering Foundry Autopilot registration assistant.>>\"{RuntimeLogRoot}\\SetupComplete.log\"",
                $"powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"{RuntimeRegistrationRoot}\\{AutomaticLaunchScriptFileName}\" >>\"{RuntimeLogRoot}\\SetupComplete.log\" 2>&1",
                "set \"FOUNDRY_AUTOPILOT_REGISTRATION_TASK_EXIT=%ERRORLEVEL%\"",
                $"echo [%date% %time%] Foundry Autopilot registration task registration exited with %FOUNDRY_AUTOPILOT_REGISTRATION_TASK_EXIT%.>>\"{RuntimeLogRoot}\\SetupComplete.log\""
            ]);
    }

    private static string BuildAutomaticLauncher()
    {
        return string.Join(
            Environment.NewLine,
            [
                "@echo off",
                "setlocal",
                $"set \"FOUNDRY_AUTOPILOT_TASK_NAME={AutomaticLaunchTaskName}\"",
                $"set \"FOUNDRY_AUTOPILOT_REGISTRATION_ROOT={RuntimeRegistrationRoot}\"",
                $"set \"FOUNDRY_AUTOPILOT_LOG_ROOT={RuntimeLogRoot}\"",
                $"set \"FOUNDRY_AUTOPILOT_RESULT={RuntimeStateRoot}\\registration-result.json\"",
                "mkdir \"%FOUNDRY_AUTOPILOT_LOG_ROOT%\" >nul 2>&1",
                "echo [%date% %time%] Starting Foundry Autopilot automatic launcher.>>\"%FOUNDRY_AUTOPILOT_LOG_ROOT%\\auto-launcher.log\"",
                "schtasks.exe /Delete /TN \"%FOUNDRY_AUTOPILOT_TASK_NAME%\" /F >>\"%FOUNDRY_AUTOPILOT_LOG_ROOT%\\auto-launcher.log\" 2>&1",
                "powershell.exe -NoProfile -ExecutionPolicy Bypass -Command \"if (Test-Path -LiteralPath $env:FOUNDRY_AUTOPILOT_RESULT) { $result = Get-Content -LiteralPath $env:FOUNDRY_AUTOPILOT_RESULT -Raw | ConvertFrom-Json; if ($result.status -eq 'completed') { exit 0 } }; exit 1\"",
                "if %ERRORLEVEL% EQU 0 (",
                "    echo [%date% %time%] Autopilot registration is already completed.>>\"%FOUNDRY_AUTOPILOT_LOG_ROOT%\\auto-launcher.log\"",
                "    exit /b 0",
                ")",
                "echo [%date% %time%] Launching Foundry Autopilot registration assistant.>>\"%FOUNDRY_AUTOPILOT_LOG_ROOT%\\auto-launcher.log\"",
                "start \"\" \"%FOUNDRY_AUTOPILOT_REGISTRATION_ROOT%\\Start-FoundryAutopilotRegistration.cmd\"",
                "exit /b 0",
                string.Empty
            ]);
    }

    private static string BuildAutomaticLaunchScript()
    {
        return string.Join(
            Environment.NewLine,
            [
                "$ErrorActionPreference = 'Stop'",
                $"$taskName = '{AutomaticLaunchTaskName}'",
                "$registrationRoot = Join-Path $env:SystemRoot 'Temp\\Foundry\\AutopilotRegistration'",
                "$logRoot = Join-Path $env:SystemRoot 'Temp\\Foundry\\Logs\\AutopilotRegistration'",
                $"$launcherPath = Join-Path $registrationRoot '{AutomaticLauncherFileName}'",
                "New-Item -Path $logRoot -ItemType Directory -Force | Out-Null",
                "$taskLogPath = Join-Path $logRoot 'task-registration.log'",
                "function Write-FoundryTaskLog {",
                "    param([Parameter(Mandatory = $true)][string]$Message)",
                "    $timestamp = [DateTimeOffset]::Now.ToString('o')",
                "    Add-Content -LiteralPath $taskLogPath -Value \"[$timestamp] $Message\"",
                "}",
                "Write-FoundryTaskLog -Message 'Registering Foundry Autopilot interactive registration task.'",
                "$cmdPath = Join-Path $env:SystemRoot 'System32\\cmd.exe'",
                "$actionArgument = '/c \"' + $launcherPath + '\"'",
                "$action = New-ScheduledTaskAction -Execute $cmdPath -Argument $actionArgument -WorkingDirectory $registrationRoot",
                "$trigger = New-ScheduledTaskTrigger -AtLogon",
                "$principal = New-ScheduledTaskPrincipal -GroupId 'BUILTIN\\Administrators' -RunLevel Highest",
                "$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable",
                "$task = New-ScheduledTask -Action $action -Trigger $trigger -Principal $principal -Settings $settings",
                "Register-ScheduledTask -TaskName $taskName -InputObject $task -Force | Out-Null",
                "Write-FoundryTaskLog -Message 'Foundry Autopilot interactive registration task registered.'",
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
            automaticLaunchTaskName = AutomaticLaunchTaskName,
            importPollingTimeoutSeconds = 900,
            importPollingIntervalSeconds = 15
        }, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        return json + Environment.NewLine;
    }
}
