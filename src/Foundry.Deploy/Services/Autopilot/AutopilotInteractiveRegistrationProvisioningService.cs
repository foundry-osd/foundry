// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Foundry.Deploy.Services.Deployment;

namespace Foundry.Deploy.Services.Autopilot;

/// <summary>
/// Stages the interactive Autopilot registration assistant into the retained Foundry runtime path.
/// </summary>
public sealed class AutopilotInteractiveRegistrationProvisioningService : IAutopilotInteractiveRegistrationProvisioningService
{
    private const string ScriptFileName = "Start-FoundryAutopilotRegistration.ps1";
    private const string LauncherFileName = "Start-FoundryAutopilotRegistration.cmd";
    private const string ConfigFileName = "config.json";

    /// <summary>Staged assistant script, relative to the retained Foundry root that Foundry.PostInstall resolves.</summary>
    internal const string RuntimeScriptRelativePath = "Runtime/AutopilotRegistration/" + ScriptFileName;

    /// <summary>Staged assistant configuration, relative to the retained Foundry root that Foundry.PostInstall resolves.</summary>
    internal const string RuntimeConfigRelativePath = "Runtime/AutopilotRegistration/" + ConfigFileName;
    private const string SetupScriptMarkerKey = "FOUNDRY AUTOPILOT REGISTRATION";
    private const string ScriptResourceName = "Foundry.Deploy.AutopilotRegistration.Start-FoundryAutopilotRegistration.ps1";
    private static readonly string RuntimeRegistrationRoot = DeploymentStorageLayout.RuntimePath(@"Runtime\AutopilotRegistration");
    private const string RuntimeLogRoot = "%SystemRoot%\\Temp\\Foundry\\Logs\\AutopilotRegistration";
    private static readonly string RuntimeStateRoot = DeploymentStorageLayout.RuntimePath(@"State\AutopilotRegistration");
    private const string FoundryBootstrapClientId = "83eb3a92-030d-49b7-881b-32a1eb3e110a";
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private readonly ISetupCompleteScriptService _setupCompleteScriptService;

    public AutopilotInteractiveRegistrationProvisioningService(ISetupCompleteScriptService setupCompleteScriptService)
    {
        _setupCompleteScriptService = setupCompleteScriptService;
    }

    /// <inheritdoc />
    public AutopilotInteractiveRegistrationProvisioningResult Provision(string targetWindowsPartitionRoot, string? assignedComputerName = null)
    {
        if (string.IsNullOrWhiteSpace(targetWindowsPartitionRoot))
        {
            throw new ArgumentException("Target Windows partition root is required.", nameof(targetWindowsPartitionRoot));
        }

        string registrationRoot = GetRegistrationRoot(targetWindowsPartitionRoot);
        string stateRoot = DeploymentStorageLayout.FromPartitionRoot(targetWindowsPartitionRoot).StateAutopilotRegistration;
        string logRoot = GetLogRoot(targetWindowsPartitionRoot);
        string scriptPath = Path.Combine(registrationRoot, ScriptFileName);
        string launcherPath = Path.Combine(registrationRoot, LauncherFileName);
        string configPath = Path.Combine(registrationRoot, ConfigFileName);
        string setupCompletePath = GetSetupScriptPath(targetWindowsPartitionRoot, "SetupComplete.cmd");
        string oobeCommandPath = GetSetupScriptPath(targetWindowsPartitionRoot, "OOBE.cmd");

        Directory.CreateDirectory(registrationRoot);
        Directory.CreateDirectory(stateRoot);
        Directory.CreateDirectory(logRoot);

        StageEmbeddedResource(ScriptResourceName, scriptPath);
        DeploymentFilePublication.WriteAllText(launcherPath, BuildLauncher(), Encoding.ASCII);
        DeploymentFilePublication.WriteAllText(configPath, BuildConfig(assignedComputerName), Utf8NoBom);

        MigrateCompletionGuard(targetWindowsPartitionRoot, stateRoot);

        // Foundry.PostInstall now runs the assistant during specialize. Setup scripts are skipped on
        // non-Enterprise editions licensed by a firmware OEM key, so earlier launch hooks are retired.
        _setupCompleteScriptService.RemoveBlock(oobeCommandPath, SetupScriptMarkerKey);
        _setupCompleteScriptService.RemoveBlock(setupCompletePath, SetupScriptMarkerKey);
        RetireCompletedLegacyRuntime(targetWindowsPartitionRoot, stateRoot, setupCompletePath, oobeCommandPath);

        return new AutopilotInteractiveRegistrationProvisioningResult
        {
            RegistrationRootPath = registrationRoot,
            ScriptPath = scriptPath,
            LauncherPath = launcherPath,
            ConfigPath = configPath,
            StateRootPath = stateRoot,
            LogRootPath = logRoot
        };
    }

    private static void RetireCompletedLegacyRuntime(string partitionRoot, string stateRoot, params string[] hooks)
    {
        string guard = Path.Combine(stateRoot, "registration-result.json");
        if (!File.Exists(guard)) { return; }
        using JsonDocument result = JsonDocument.Parse(File.ReadAllText(guard));
        if (!result.RootElement.TryGetProperty("status", out JsonElement status) || status.GetString() != "completed") { return; }
        if (hooks.Any(path => File.Exists(path) && File.ReadAllText(path).Contains(
            @"\Temp\Foundry\AutopilotRegistration", StringComparison.OrdinalIgnoreCase))) { return; }
        string legacyRoot = Path.Combine(DeploymentStorageLayout.FromPartitionRoot(partitionRoot).Root, "AutopilotRegistration");
        foreach (string name in new[] { ScriptFileName, LauncherFileName, "Start-FoundryAutopilotRegistrationOobe.cmd",
            "Wait-FoundryAutopilotRegistrationOobe.ps1", "Start-FoundryAutopilotRegistrationForeground.ps1", "ServiceUI.exe", ConfigFileName })
        {
            string path = Path.Combine(legacyRoot, name);
            if (File.Exists(path)) { File.Delete(path); }
        }
        // Keep the legacy completion guard: independently retained hooks must still suppress re-registration.
    }

    private static void MigrateCompletionGuard(string partitionRoot, string stateRoot)
    {
        string legacy = Path.Combine(DeploymentStorageLayout.FromPartitionRoot(partitionRoot).Root,
            "AutopilotRegistration", "State", "registration-result.json");
        if (!File.Exists(legacy)) { return; }
        using JsonDocument result = JsonDocument.Parse(File.ReadAllText(legacy));
        if (result.RootElement.TryGetProperty("status", out JsonElement status) && status.GetString() == "completed")
        {
            DeploymentFilePublication.WriteAllText(Path.Combine(stateRoot, "registration-result.json"),
                result.RootElement.GetRawText(), Utf8NoBom);
        }
        // The old guard remains until every possible legacy hook has been retired.
    }

    private static string GetRegistrationRoot(string targetWindowsPartitionRoot)
    {
        return DeploymentStorageLayout.FromPartitionRoot(targetWindowsPartitionRoot).RuntimeAutopilotRegistration;
    }

    private static string GetLogRoot(string targetWindowsPartitionRoot)
    {
        return Path.Combine(targetWindowsPartitionRoot, "Windows", "Temp", "Foundry", "Logs", "AutopilotRegistration");
    }

    private static string GetSetupScriptPath(string targetWindowsPartitionRoot, string fileName)
    {
        return Path.Combine(targetWindowsPartitionRoot, "Windows", "Setup", "Scripts", fileName);
    }

    private static void StageEmbeddedResource(string resourceName, string destinationPath)
    {
        Assembly assembly = typeof(AutopilotInteractiveRegistrationProvisioningService).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            throw new InvalidOperationException($"Embedded Autopilot registration resource '{resourceName}' was not found.");
        }

        using FileStream destination = File.Create(destinationPath);
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

    private static string BuildConfig(string? assignedComputerName)
    {
        string json = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            provisioningMode = "interactiveHardwareHashUpload",
            assignedComputerName = string.IsNullOrWhiteSpace(assignedComputerName) ? null : assignedComputerName,
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
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

        return json + Environment.NewLine;
    }
}
