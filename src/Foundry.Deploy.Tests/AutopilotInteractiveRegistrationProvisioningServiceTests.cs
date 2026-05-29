using System.Text.Json;
using Foundry.Deploy.Services.Autopilot;
using Foundry.Deploy.Services.Deployment;

namespace Foundry.Deploy.Tests;

public sealed class AutopilotInteractiveRegistrationProvisioningServiceTests
{
    [Fact]
    public void Provision_StagesAssistantLauncherConfigAndOobeHook()
    {
        string windowsRoot = CreateWindowsRoot();
        var service = CreateService();

        AutopilotInteractiveRegistrationProvisioningResult result = service.Provision(windowsRoot);

        string registrationRoot = Path.Combine(windowsRoot, "Windows", "Temp", "Foundry", "AutopilotRegistration");
        string logRoot = Path.Combine(windowsRoot, "Windows", "Temp", "Foundry", "Logs", "AutopilotRegistration");
        Assert.Equal(registrationRoot, result.RegistrationRootPath);
        Assert.Equal(Path.Combine(registrationRoot, "Start-FoundryAutopilotRegistration.ps1"), result.ScriptPath);
        Assert.Equal(Path.Combine(registrationRoot, "Start-FoundryAutopilotRegistration.cmd"), result.LauncherPath);
        Assert.Equal(Path.Combine(registrationRoot, "Start-FoundryAutopilotRegistrationOobe.cmd"), result.OobeLauncherPath);
        Assert.Equal(Path.Combine(registrationRoot, "Wait-FoundryAutopilotRegistrationOobe.ps1"), result.OobeWaiterPath);
        Assert.Equal(Path.Combine(registrationRoot, "ServiceUI.exe"), result.ServiceUiPath);
        Assert.Equal(Path.Combine(windowsRoot, "Windows", "Setup", "Scripts", "OOBE.cmd"), result.OobeCommandPath);
        Assert.Equal(Path.Combine(registrationRoot, "config.json"), result.ConfigPath);
        Assert.Equal(logRoot, result.LogRootPath);
        Assert.True(File.Exists(result.ScriptPath));
        Assert.True(File.Exists(result.LauncherPath));
        Assert.True(File.Exists(result.OobeLauncherPath));
        Assert.True(File.Exists(result.OobeWaiterPath));
        Assert.True(File.Exists(result.ServiceUiPath));
        Assert.True(File.Exists(result.OobeCommandPath));
        Assert.True(File.Exists(result.ConfigPath));
        Assert.True(Directory.Exists(Path.Combine(registrationRoot, "State")));
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
        Assert.Contains("%SystemRoot%\\Temp\\Foundry\\AutopilotRegistration\\Start-FoundryAutopilotRegistration.ps1", launcher);
        Assert.Contains("-ConfigPath \"%SystemRoot%\\Temp\\Foundry\\AutopilotRegistration\\config.json\"", launcher);
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
        Assert.Contains("Get-FoundryServiceUiTargetProcess", oobeWaiter);
        Assert.Contains("-process:$targetProcessName", oobeWaiter);
        Assert.Contains("Before ServiceUI target selection", oobeWaiter);
        Assert.Contains("Selected ServiceUI target process", oobeWaiter);
        Assert.Contains("Launching assistant through ServiceUI", oobeWaiter);
        Assert.DoesNotContain("Falling back to direct launch", oobeWaiter);
        Assert.DoesNotContain("Start-Process -FilePath $powershellPath", oobeWaiter);
        Assert.Contains("oobe-sessiondiag.log", oobeWaiter);
        Assert.Contains("query session", oobeWaiter);
        Assert.Contains("Before assistant launch", oobeWaiter);
        Assert.Contains("After assistant launch", oobeWaiter);
        Assert.Contains("OOBE waiter failed.", oobeWaiter);
        Assert.Contains("Start-Sleep -Seconds $stableSeconds", oobeWaiter);
        Assert.Contains("-STA", oobeWaiter);
        Assert.Contains("-WindowStyle", oobeWaiter);
        Assert.Contains("Hidden", oobeWaiter);
        Assert.Contains("Start-FoundryAutopilotRegistration.ps1", oobeWaiter);

        string oobeCommand = File.ReadAllText(result.OobeCommandPath);
        Assert.Contains("REM >>> FOUNDRY AUTOPILOT REGISTRATION BEGIN", oobeCommand);
        Assert.Contains(
            "call \"%SystemRoot%\\Temp\\Foundry\\AutopilotRegistration\\Start-FoundryAutopilotRegistrationOobe.cmd\"",
            oobeCommand);
        Assert.Contains("REM <<< FOUNDRY AUTOPILOT REGISTRATION END", oobeCommand);
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
        Assert.Contains("Waiting for network connectivity.", script);
        Assert.Contains("Retrying Microsoft sign-in request", script);
        Assert.Contains("Request-DeviceCodeToken", script);
        Assert.Contains("ErrorDetails.Message", script);
        Assert.Contains("authorization_pending", script);
        Assert.Contains("Get-AutopilotHardwareIdentity", script);
        Assert.Contains("Import-AutopilotDeviceIdentity", script);
        Assert.Contains("Test-AutopilotDeviceReadiness", script);
        Assert.Contains("Find-AutopilotDeviceBySerialNumber", script);
        Assert.Contains("Update-AutopilotDeviceGroupTag", script);
        Assert.Contains("Should-ContinueVisibilityWaitAfterImportError", script);
        Assert.Contains("Invoke-GraphRequest", script);
        Assert.Contains("Test-RegistrationAlreadyCompleted", script);
        Assert.Contains("deviceManagement/importedWindowsAutopilotDeviceIdentities/import", script);
        Assert.Contains("deviceManagement/windowsAutopilotDeviceIdentities", script);
        Assert.Contains("updateDeviceProperties", script);
        Assert.Contains("AlreadyAssigned", script);
        Assert.Contains("AlreadyExists", script);
        Assert.DoesNotContain("Install-Module", script);
        Assert.DoesNotContain("Connect-MgGraph", script);
        Assert.DoesNotContain("Get-WindowsAutopilotInfo", script);
        Assert.DoesNotContain("WindowsAutopilotIntune", script);
    }

    [Fact]
    public void Provision_StagedScriptUsesTwoStepWpfFlow()
    {
        string windowsRoot = CreateWindowsRoot();
        var service = CreateService();

        AutopilotInteractiveRegistrationProvisioningResult result = service.Provision(windowsRoot);

        string script = File.ReadAllText(result.ScriptPath);
        Assert.Contains("PresentationFramework", script);
        Assert.Contains("Start-FoundryAutopilotRegistrationUi", script);
        Assert.Contains("Show-AuthenticationStep", script);
        Assert.Contains("Show-UploadStep", script);
        Assert.Contains("Start-AuthenticationFlow", script);
        Assert.Contains("Add_ContentRendered", script);
        Assert.Contains("AuthenticationProgressBar", script);
        Assert.Contains("AuthenticationStatusTextBlock", script);
        Assert.Contains("Code expires in {0} seconds.", script);
        Assert.Contains("Update-AuthenticationCountdown", script);
        Assert.Contains("UploadProgressBar", script);
        Assert.Contains("UploadStatusTextBlock", script);
        Assert.Contains("IsIndeterminate", script);
        Assert.Contains("Set-UploadControlsEnabled", script);
        Assert.Contains("Content=\"Upload\"", script);
        Assert.Contains("Group tag", script);
        Assert.Contains("Foundry OSD - Interactive hardware hash upload", script);
        Assert.Contains("Foundry OSD - Sign in to Microsoft", script);
        Assert.Contains("Foundry OSD - Upload hardware hash", script);
        Assert.Contains("UseLayoutRounding=\"True\"", script);
        Assert.Contains("Stretch=\"Uniform\"", script);
        Assert.Contains("RenderOptions.BitmapScalingMode=\"HighQuality\"", script);
        Assert.Contains("<Run Text=\" \" />", script);
        Assert.Contains("Choose a group tag, then upload this device hardware hash to Microsoft Intune.", script);
        Assert.Contains("Waiting for device registration in Microsoft Intune.", script);
        Assert.Contains("Restarting in {0} seconds.", script);
        Assert.Contains("Restarting now.", script);
        Assert.Contains("shutdown.exe", script);
        Assert.Contains("Width=\"420\"", script);
        Assert.Contains("Height=\"560\"", script);
        Assert.Contains("ResizeMode=\"NoResize\"", script);
        Assert.Contains("FontSize=\"16\"", script);
        Assert.Contains("FontSize=\"32\"", script);
        Assert.Contains("MinWidth=\"140\"", script);
        Assert.Contains("MinHeight=\"32\"", script);
        Assert.DoesNotContain("Read-Host", script);
        Assert.DoesNotContain("Write-Host", script);
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
