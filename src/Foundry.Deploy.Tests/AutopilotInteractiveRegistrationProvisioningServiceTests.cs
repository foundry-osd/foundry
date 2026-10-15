// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Foundry.Deploy.Services.Autopilot;
using Foundry.Deploy.Services.Deployment;

namespace Foundry.Deploy.Tests;

public sealed class AutopilotInteractiveRegistrationProvisioningServiceTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("FINAL-PC")]
    public void Provision_StagesOnlyTheSuppliedFinalComputerName(string? assignedComputerName)
    {
        AutopilotInteractiveRegistrationProvisioningResult result = CreateService().Provision(CreateWindowsRoot(), assignedComputerName);
        using JsonDocument config = JsonDocument.Parse(File.ReadAllText(result.ConfigPath));
        if (assignedComputerName is null)
        {
            Assert.False(config.RootElement.TryGetProperty("assignedComputerName", out _));
        }
        else
        {
            Assert.Equal(assignedComputerName, config.RootElement.GetProperty("assignedComputerName").GetString());
        }
    }

    [Theory]
    [InlineData("completed", true)]
    [InlineData("failed", false)]
    public void Provision_MigratesCompletedLegacyGuard(string status, bool expectedGuard)
    {
        string windowsRoot = CreateWindowsRoot();
        var layout = DeploymentStorageLayout.FromPartitionRoot(windowsRoot);
        string legacyDirectory = Path.Combine(layout.Root, "AutopilotRegistration", "State");
        Directory.CreateDirectory(legacyDirectory);
        string legacyRoot = Path.GetDirectoryName(legacyDirectory)!;
        string legacyScript = Path.Combine(legacyRoot, "Start-FoundryAutopilotRegistration.ps1");
        File.WriteAllText(legacyScript, "old helper");
        string unrelated = Path.Combine(legacyRoot, "vendor.txt");
        File.WriteAllText(unrelated, "preserve");
        string legacyGuard = Path.Combine(legacyDirectory, "registration-result.json");
        File.WriteAllText(legacyGuard, JsonSerializer.Serialize(new { status }));
        AutopilotInteractiveRegistrationProvisioningResult result = CreateService().Provision(windowsRoot);
        string guard = Path.Combine(result.StateRootPath, "registration-result.json");
        Assert.Equal(expectedGuard, File.Exists(guard));
        Assert.True(File.Exists(legacyGuard));
        Assert.Equal(!expectedGuard, File.Exists(legacyScript));
        Assert.True(File.Exists(unrelated));
        if (expectedGuard)
        {
            using JsonDocument state = JsonDocument.Parse(File.ReadAllText(guard));
            Assert.Equal("completed", state.RootElement.GetProperty("status").GetString());
            CreateService().Provision(windowsRoot);
            Assert.Equal("completed", JsonDocument.Parse(File.ReadAllText(guard)).RootElement.GetProperty("status").GetString());
        }
        Assert.Empty(Directory.GetFiles(result.StateRootPath, "*.tmp"));
    }

    [Fact]
    public void Provision_StagesAssistantWithoutOobeLaunchChain()
    {
        string windowsRoot = CreateWindowsRoot();
        var service = CreateService();

        AutopilotInteractiveRegistrationProvisioningResult result = service.Provision(windowsRoot);

        string registrationRoot = Path.Combine(windowsRoot, "Windows", "Temp", "Foundry", "Runtime", "AutopilotRegistration");
        string logRoot = Path.Combine(windowsRoot, "Windows", "Temp", "Foundry", "Logs", "AutopilotRegistration");
        Assert.Equal(registrationRoot, result.RegistrationRootPath);
        Assert.Equal(Path.Combine(registrationRoot, "Start-FoundryAutopilotRegistration.ps1"), result.ScriptPath);
        Assert.Equal(Path.Combine(registrationRoot, "Start-FoundryAutopilotRegistration.cmd"), result.LauncherPath);
        Assert.Equal(Path.Combine(registrationRoot, "config.json"), result.ConfigPath);
        Assert.Equal(logRoot, result.LogRootPath);
        Assert.True(File.Exists(result.ScriptPath));
        Assert.True(File.Exists(result.LauncherPath));
        Assert.True(File.Exists(result.ConfigPath));
        Assert.True(Directory.Exists(DeploymentStorageLayout.FromPartitionRoot(windowsRoot).StateAutopilotRegistration));
        Assert.True(Directory.Exists(logRoot));
        Assert.Equal(["config.json", "Start-FoundryAutopilotRegistration.cmd", "Start-FoundryAutopilotRegistration.ps1"],
            Directory.GetFiles(registrationRoot).Select(Path.GetFileName).Order(StringComparer.OrdinalIgnoreCase));
        Assert.False(File.Exists(Path.Combine(windowsRoot, "Windows", "Setup", "Scripts", "OOBE.cmd")));
    }

    [Fact]
    public void Provision_StagesFilesAtThePathsThePostInstallPlanReferences()
    {
        string windowsRoot = CreateWindowsRoot();

        AutopilotInteractiveRegistrationProvisioningResult result = CreateService().Provision(windowsRoot);

        string foundryRoot = DeploymentStorageLayout.FromPartitionRoot(windowsRoot).Root;
        Assert.Equal(result.ScriptPath, Path.Combine(foundryRoot,
            AutopilotInteractiveRegistrationProvisioningService.RuntimeScriptRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        Assert.Equal(result.ConfigPath, Path.Combine(foundryRoot,
            AutopilotInteractiveRegistrationProvisioningService.RuntimeConfigRelativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    [Fact]
    public void Provision_WritesSanitizedConfigWithDeviceCodeSettings()
    {
        string windowsRoot = CreateWindowsRoot();
        var service = CreateService();

        AutopilotInteractiveRegistrationProvisioningResult result = service.Provision(windowsRoot);

        using JsonDocument config = JsonDocument.Parse(File.ReadAllText(result.ConfigPath));
        Assert.Equal(1, config.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("interactiveHardwareHashUpload", config.RootElement.GetProperty("provisioningMode").GetString());
        Assert.Equal("common", config.RootElement.GetProperty("tenant").GetString());
        Assert.Equal("83eb3a92-030d-49b7-881b-32a1eb3e110a", config.RootElement.GetProperty("clientId").GetString());
        Assert.Contains(
            "DeviceManagementServiceConfig.ReadWrite.All",
            config.RootElement.GetProperty("scopes").EnumerateArray().Select(scope => scope.GetString()));
        Assert.False(config.RootElement.TryGetProperty("clientSecret", out _));
        Assert.False(config.RootElement.TryGetProperty("certificatePfxSecret", out _));
        Assert.False(config.RootElement.TryGetProperty("groupTag", out _));
    }

    [Fact]
    public void Provision_WritesManualLauncher()
    {
        string windowsRoot = CreateWindowsRoot();
        var service = CreateService();

        AutopilotInteractiveRegistrationProvisioningResult result = service.Provision(windowsRoot);

        string launcher = File.ReadAllText(result.LauncherPath);
        Assert.Contains("@echo off", launcher);
        Assert.Contains("%SystemRoot%\\Temp\\Foundry\\Logs\\AutopilotRegistration", launcher);
        Assert.Contains("launcher.log", launcher);
        Assert.Contains("-STA", launcher);
        Assert.Contains("%SystemRoot%\\Temp\\Foundry\\Runtime\\AutopilotRegistration\\Start-FoundryAutopilotRegistration.ps1", launcher);
        Assert.Contains("-ConfigPath \"%SystemRoot%\\Temp\\Foundry\\Runtime\\AutopilotRegistration\\config.json\"", launcher);
    }

    [Fact]
    public void Provision_RemovesObsoleteOobeLaunchBlock()
    {
        string windowsRoot = CreateWindowsRoot();
        string oobePath = Path.Combine(windowsRoot, "Windows", "Setup", "Scripts", "OOBE.cmd");
        Directory.CreateDirectory(Path.GetDirectoryName(oobePath)!);
        File.WriteAllText(oobePath, string.Join(Environment.NewLine,
        [
            "@echo off",
            "REM >>> FOUNDRY AUTOPILOT REGISTRATION BEGIN",
            @"call ""%SystemRoot%\Temp\Foundry\Runtime\AutopilotRegistration\Start-FoundryAutopilotRegistrationOobe.cmd""",
            "REM <<< FOUNDRY AUTOPILOT REGISTRATION END",
            "echo existing customization"
        ]));

        CreateService().Provision(windowsRoot);

        string oobe = File.ReadAllText(oobePath);
        Assert.DoesNotContain("FOUNDRY AUTOPILOT REGISTRATION", oobe);
        Assert.Contains("echo existing customization", oobe);
    }

    [Fact]
    public void Provision_RemovesObsoleteSetupCompleteLaunchBlock()
    {
        string windowsRoot = CreateWindowsRoot();
        string setupCompletePath = Path.Combine(windowsRoot, "Windows", "Setup", "Scripts", "SetupComplete.cmd");
        Directory.CreateDirectory(Path.GetDirectoryName(setupCompletePath)!);
        File.WriteAllText(
            setupCompletePath,
            string.Join(
                Environment.NewLine,
                [
                    "@echo off",
                    "REM >>> FOUNDRY AUTOPILOT REGISTRATION BEGIN",
                    "old-autopilot-registration-command",
                    "REM <<< FOUNDRY AUTOPILOT REGISTRATION END",
                    "echo existing customization"
                ]));
        var service = CreateService();

        service.Provision(windowsRoot);

        string setupComplete = File.ReadAllText(setupCompletePath);

        Assert.DoesNotContain("old-autopilot-registration-command", setupComplete);
        Assert.Contains("echo existing customization", setupComplete);
        Assert.DoesNotContain("REM >>> FOUNDRY AUTOPILOT REGISTRATION BEGIN", setupComplete);
    }

    [Fact]
    public void Provision_StagedScriptContainsExpectedFlowWithoutExternalModuleDependencies()
    {
        string windowsRoot = CreateWindowsRoot();
        var service = CreateService();

        AutopilotInteractiveRegistrationProvisioningResult result = service.Provision(windowsRoot);

        string script = File.ReadAllText(result.ScriptPath);
        Assert.Contains("Request-DeviceCode", script);
        Assert.Contains("Start-AuthenticationDeviceCodeRequest", script);
        Assert.Contains("Test-TransientHttpFailure", script);
        Assert.Contains("TransientFailure", script);
        Assert.Contains("Request-DeviceCodeToken", script);
        Assert.Contains("ErrorDetails.Message", script);
        Assert.Contains("authorization_pending", script);
        Assert.Contains("expired_token", script);
        Assert.Contains("Get-AutopilotHardwareIdentity", script);
        Assert.Contains("Import-AutopilotDeviceIdentity", script);
        Assert.Contains("Test-AutopilotDeviceReadiness", script);
        Assert.Contains("Find-AutopilotDeviceBySerialNumber", script);
        Assert.Contains("Should-ContinueVisibilityWaitAfterImportError", script);
        Assert.Contains("Invoke-GraphRequest", script);
        Assert.Contains("Test-RegistrationAlreadyCompleted", script);
        Assert.Contains("deviceManagement/importedWindowsAutopilotDeviceIdentities/import", script);
        Assert.Contains("deviceManagement/windowsAutopilotDeviceIdentities", script);
        Assert.Contains("updateDeviceProperties", script);
        Assert.Contains("AlreadyAssigned", script);
        Assert.Contains("AlreadyExists", script);
        // Setup is still in specialize; restarting here would interrupt Windows installation.
        Assert.DoesNotContain("shutdown.exe", script);
        Assert.DoesNotContain("Read-Host", script);
        Assert.DoesNotContain("Write-Host", script);
        Assert.DoesNotContain("Install-Module", script);
        Assert.DoesNotContain("Connect-MgGraph", script);
        Assert.DoesNotContain("Get-WindowsAutopilotInfo", script);
        Assert.DoesNotContain("WindowsAutopilotIntune", script);
        Assert.DoesNotContain("Wait-AutopilotDeviceGroupTag", script);
        Assert.DoesNotContain("Start-Sleep -Seconds $intervalSeconds", script);
    }

    private static string CreateWindowsRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "FoundryDeployTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static AutopilotInteractiveRegistrationProvisioningService CreateService()
    {
        return new AutopilotInteractiveRegistrationProvisioningService(new SetupCompleteScriptService());
    }
}
