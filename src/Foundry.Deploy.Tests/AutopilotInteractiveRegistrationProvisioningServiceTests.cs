// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Foundry.Deploy.Services.Autopilot;
using Foundry.Deploy.Services.Deployment;

namespace Foundry.Deploy.Tests;

public sealed class AutopilotInteractiveRegistrationProvisioningServiceTests
{
    [Fact]
    public void Provision_WhenHookPublicationFails_PreservesPreviousLaunchHooks()
    {
        string root = CreateWindowsRoot();
        string scripts = Path.Combine(root, "Windows", "Setup", "Scripts");
        string setup = Path.Combine(scripts, "SetupComplete.cmd");
        string oobe = Path.Combine(scripts, "OOBE.cmd");
        var hooks = new SetupCompleteScriptService();
        hooks.EnsureBlock(setup, "FOUNDRY AUTOPILOT REGISTRATION", "call previous-setup.cmd");
        hooks.EnsureBlock(oobe, "FOUNDRY AUTOPILOT REGISTRATION", "call previous-oobe.cmd");
        string beforeSetup = File.ReadAllText(setup);
        string beforeOobe = File.ReadAllText(oobe);
        var service = new AutopilotInteractiveRegistrationProvisioningService(new FailingHookPublicationService());

        Assert.Throws<IOException>(() => service.Provision(root));

        Assert.Equal(beforeSetup, File.ReadAllText(setup));
        Assert.Equal(beforeOobe, File.ReadAllText(oobe));
    }

    private sealed class FailingHookPublicationService : ISetupCompleteScriptService
    {
        public string EnsureBlock(string path, string marker, string body) => throw new IOException("Simulated publication failure.");
        public string RemoveBlock(string path, string marker) => new SetupCompleteScriptService().RemoveBlock(path, marker);
    }

    [Fact]
    public void Provision_WhenInterruptedBetweenHookUpdates_RoutesLegacyLauncherToSharedRuntime()
    {
        string root = CreateWindowsRoot();
        string legacyRoot = Path.Combine(DeploymentStorageLayout.FromPartitionRoot(root).Root, "AutopilotRegistration");
        Directory.CreateDirectory(legacyRoot);
        string[] names = ["Start-FoundryAutopilotRegistration.cmd", "Start-FoundryAutopilotRegistrationOobe.cmd"];
        foreach (string name in names) File.WriteAllText(Path.Combine(legacyRoot, name), "call old-helper.ps1");
        string setup = Path.Combine(root, "Windows", "Setup", "Scripts", "SetupComplete.cmd");
        new SetupCompleteScriptService().EnsureBlock(setup, "FOUNDRY AUTOPILOT REGISTRATION",
            @"call %SystemRoot%\Temp\Foundry\AutopilotRegistration\Start-FoundryAutopilotRegistration.cmd");

        Assert.Throws<IOException>(() => new AutopilotInteractiveRegistrationProvisioningService(new InterruptedHookRetirementService()).Provision(root));

        Assert.Contains(@"\Temp\Foundry\AutopilotRegistration", File.ReadAllText(setup));
        Assert.Contains(@"\Runtime\AutopilotRegistration", File.ReadAllText(Path.Combine(Path.GetDirectoryName(setup)!, "OOBE.cmd")));
        foreach (string name in names)
            Assert.Contains(@"%SystemRoot%\Temp\Foundry\Runtime\AutopilotRegistration\" + name, File.ReadAllText(Path.Combine(legacyRoot, name)));
    }

    private sealed class InterruptedHookRetirementService : ISetupCompleteScriptService
    {
        public string EnsureBlock(string path, string marker, string body) => new SetupCompleteScriptService().EnsureBlock(path, marker, body);
        public string RemoveBlock(string path, string marker) => throw new IOException("Simulated interruption after publishing the new hook.");
    }

    [Theory]
    [InlineData("completed", true)]
    [InlineData("failed", false)]
    public void Provision_MigratesCompletedLegacyGuardBeforeReplacingHooks(string status, bool expectedGuard)
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
    public void Provision_StagesAssistantLauncherConfigAndOobeHook()
    {
        string windowsRoot = CreateWindowsRoot();
        var service = CreateService();

        AutopilotInteractiveRegistrationProvisioningResult result = service.Provision(windowsRoot);

        string registrationRoot = Path.Combine(windowsRoot, "Windows", "Temp", "Foundry", "Runtime", "AutopilotRegistration");
        string logRoot = Path.Combine(windowsRoot, "Windows", "Temp", "Foundry", "Logs", "AutopilotRegistration");
        Assert.Equal(registrationRoot, result.RegistrationRootPath);
        Assert.Equal(Path.Combine(registrationRoot, "Start-FoundryAutopilotRegistration.ps1"), result.ScriptPath);
        Assert.Equal(Path.Combine(registrationRoot, "Start-FoundryAutopilotRegistration.cmd"), result.LauncherPath);
        Assert.Equal(Path.Combine(registrationRoot, "Start-FoundryAutopilotRegistrationOobe.cmd"), result.OobeLauncherPath);
        Assert.Equal(Path.Combine(registrationRoot, "Wait-FoundryAutopilotRegistrationOobe.ps1"), result.OobeWaiterPath);
        Assert.Equal(Path.Combine(registrationRoot, "Start-FoundryAutopilotRegistrationForeground.ps1"), result.ForegroundWrapperPath);
        Assert.Equal(Path.Combine(registrationRoot, "ServiceUI.exe"), result.ServiceUiPath);
        Assert.Equal(Path.Combine(windowsRoot, "Windows", "Setup", "Scripts", "OOBE.cmd"), result.OobeCommandPath);
        Assert.Equal(Path.Combine(registrationRoot, "config.json"), result.ConfigPath);
        Assert.Equal(logRoot, result.LogRootPath);
        Assert.True(File.Exists(result.ScriptPath));
        Assert.True(File.Exists(result.LauncherPath));
        Assert.True(File.Exists(result.OobeLauncherPath));
        Assert.True(File.Exists(result.OobeWaiterPath));
        Assert.True(File.Exists(result.ForegroundWrapperPath));
        Assert.True(File.Exists(result.ServiceUiPath));
        Assert.True(File.Exists(result.OobeCommandPath));
        Assert.True(File.Exists(result.ConfigPath));
        Assert.True(Directory.Exists(DeploymentStorageLayout.FromPartitionRoot(windowsRoot).StateAutopilotRegistration));
        Assert.True(Directory.Exists(logRoot));
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
    public void Provision_WritesOobeLauncherAndOobeCommand()
    {
        string windowsRoot = CreateWindowsRoot();
        var service = CreateService();

        AutopilotInteractiveRegistrationProvisioningResult result = service.Provision(windowsRoot);

        string oobeLauncher = File.ReadAllText(result.OobeLauncherPath);
        Assert.Contains("oobe-launcher.log", oobeLauncher);
        Assert.Contains("WindowsPowerShell\\v1.0\\powershell.exe", oobeLauncher);
        Assert.Contains("start \"\"", oobeLauncher);
        Assert.Contains("-WindowStyle Hidden", oobeLauncher);
        Assert.Contains("Wait-FoundryAutopilotRegistrationOobe.ps1", oobeLauncher);
        Assert.Contains("exit /b 0", oobeLauncher);

        string oobeWaiter = File.ReadAllText(result.OobeWaiterPath);
        Assert.Contains("CloudExperienceHost", oobeWaiter);
        Assert.Contains("CloudExperienceHostBroker", oobeWaiter);
        Assert.Contains("UserOOBEBroker", oobeWaiter);
        Assert.Contains("ServiceUI.exe", oobeWaiter);
        Assert.Contains("WTSGetActiveConsoleSessionId", oobeWaiter);
        Assert.Contains("Get-FoundryActiveConsoleSessionId", oobeWaiter);
        Assert.Contains("[uint32]::MaxValue", oobeWaiter);
        Assert.Contains("-session:$activeSessionId", oobeWaiter);
        Assert.Contains("Start-FoundryAutopilotRegistrationForeground.ps1", oobeWaiter);
        Assert.Contains("-RegistrationScriptPath", oobeWaiter);
        Assert.Contains("Launching assistant through ServiceUI in active console session", oobeWaiter);
        Assert.Contains("active console session:", oobeWaiter);
        Assert.Contains("Timed out while waiting for active console session.", oobeWaiter);
        Assert.Contains("-WindowStyle Hidden", oobeWaiter);
        Assert.DoesNotContain("OOBEComplete", oobeWaiter);
        Assert.DoesNotContain("oobenotification.dll", oobeWaiter);
        Assert.DoesNotContain("Falling back to direct launch", oobeWaiter);
        Assert.DoesNotContain("Start-Process -FilePath $powershellPath", oobeWaiter);
        Assert.DoesNotContain("-process:", oobeWaiter);
        Assert.DoesNotContain("Get-FoundryServiceUiTargetProcess", oobeWaiter);
        Assert.DoesNotContain("serviceUiTargetProcessNames", oobeWaiter);
        Assert.Contains("oobe-sessiondiag.log", oobeWaiter);
        Assert.Contains("query session", oobeWaiter);
        Assert.Contains("Before assistant launch", oobeWaiter);
        Assert.Contains("After assistant launch", oobeWaiter);
        Assert.Contains("OOBE waiter failed.", oobeWaiter);
        Assert.DoesNotContain("$stableSeconds", oobeWaiter);
        Assert.Contains("-STA", oobeWaiter);
        Assert.Contains("-WindowStyle", oobeWaiter);
        Assert.Contains("Hidden", oobeWaiter);
        Assert.Contains("Start-FoundryAutopilotRegistration.ps1", oobeWaiter);

        string foregroundWrapper = File.ReadAllText(result.ForegroundWrapperPath);
        Assert.Contains("GetForegroundWindow", foregroundWrapper);
        Assert.Contains("SetForegroundWindow", foregroundWrapper);
        Assert.Contains("Invoke-FoundryActivateOobeWindow", foregroundWrapper);
        Assert.Contains("SendShiftF10", foregroundWrapper);
        Assert.Contains("Wait-FoundryOobeForegroundAccess", foregroundWrapper);
        Assert.Contains("Close-FoundryOobeCommandPrompt", foregroundWrapper);
        Assert.Contains("& $RegistrationScriptPath -ConfigPath $ConfigPath", foregroundWrapper);

        string oobeCommand = File.ReadAllText(result.OobeCommandPath);
        Assert.Contains("REM >>> FOUNDRY AUTOPILOT REGISTRATION BEGIN", oobeCommand);
        Assert.Contains(
            "call \"%SystemRoot%\\Temp\\Foundry\\Runtime\\AutopilotRegistration\\Start-FoundryAutopilotRegistrationOobe.cmd\"",
            oobeCommand);
        Assert.Contains("REM <<< FOUNDRY AUTOPILOT REGISTRATION END", oobeCommand);
    }

    [Fact]
    public void Provision_WritesConditionDrivenOobeForegroundUnlock()
    {
        string windowsRoot = CreateWindowsRoot();
        var service = CreateService();

        AutopilotInteractiveRegistrationProvisioningResult result = service.Provision(windowsRoot);

        string foregroundWrapper = File.ReadAllText(result.ForegroundWrapperPath);
        Assert.Contains("$foregroundDeadline = [DateTimeOffset]::UtcNow.AddMinutes(2)", foregroundWrapper);
        Assert.Contains("while ([DateTimeOffset]::UtcNow -lt $foregroundDeadline)", foregroundWrapper);
        Assert.Contains("$_.MainWindowHandle -ne 0", foregroundWrapper);
        Assert.Contains("Timed out while waiting for OOBE foreground access.", foregroundWrapper);

        int timeoutGuardIndex = foregroundWrapper.IndexOf(
            "if (-not $commandPrompt)",
            StringComparison.Ordinal);
        int assistantLaunchIndex = foregroundWrapper.IndexOf(
            "& $RegistrationScriptPath -ConfigPath $ConfigPath",
            StringComparison.Ordinal);

        Assert.True(timeoutGuardIndex >= 0);
        Assert.True(assistantLaunchIndex > timeoutGuardIndex);
    }

    [Fact]
    public void Provision_DoesNotDuplicateOobeLaunchBlock()
    {
        string windowsRoot = CreateWindowsRoot();
        var service = CreateService();

        service.Provision(windowsRoot);
        AutopilotInteractiveRegistrationProvisioningResult result = service.Provision(windowsRoot);

        string oobeCommand = File.ReadAllText(result.OobeCommandPath);

        Assert.Equal(1, CountOccurrences(oobeCommand, "REM >>> FOUNDRY AUTOPILOT REGISTRATION BEGIN"));
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
        Assert.Equal(0, CountOccurrences(setupComplete, "REM >>> FOUNDRY AUTOPILOT REGISTRATION BEGIN"));
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
        Assert.Contains("Update-AutopilotDeviceGroupTag", script);
        Assert.Contains("Should-ContinueVisibilityWaitAfterImportError", script);
        Assert.Contains("UploadGroupTagUpdateRequested", script);
        Assert.Contains("Invoke-GraphRequest", script);
        Assert.Contains("Test-RegistrationAlreadyCompleted", script);
        Assert.Contains("deviceManagement/importedWindowsAutopilotDeviceIdentities/import", script);
        Assert.Contains("deviceManagement/windowsAutopilotDeviceIdentities", script);
        Assert.Contains("updateDeviceProperties", script);
        Assert.Contains("AlreadyAssigned", script);
        Assert.Contains("AlreadyExists", script);
        Assert.Contains("shutdown.exe", script);
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

    private static int CountOccurrences(string value, string expected)
    {
        int count = 0;
        int index = 0;

        while ((index = value.IndexOf(expected, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += expected.Length;
        }

        return count;
    }
}
