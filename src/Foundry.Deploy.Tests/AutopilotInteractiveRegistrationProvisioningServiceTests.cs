using System.Text.Json;
using Foundry.Deploy.Services.Autopilot;
using Foundry.Deploy.Services.Deployment;

namespace Foundry.Deploy.Tests;

public sealed class AutopilotInteractiveRegistrationProvisioningServiceTests
{
    [Fact]
    public void Provision_StagesAssistantLauncherConfigAndRuntimeFolders()
    {
        string windowsRoot = CreateWindowsRoot();
        var service = CreateService();

        AutopilotInteractiveRegistrationProvisioningResult result = service.Provision(windowsRoot);

        string registrationRoot = Path.Combine(windowsRoot, "Windows", "Temp", "Foundry", "AutopilotRegistration");
        string logRoot = Path.Combine(windowsRoot, "Windows", "Temp", "Foundry", "Logs", "AutopilotRegistration");
        Assert.Equal(registrationRoot, result.RegistrationRootPath);
        Assert.Equal(Path.Combine(registrationRoot, "Start-FoundryAutopilotRegistration.ps1"), result.ScriptPath);
        Assert.Equal(Path.Combine(registrationRoot, "Start-FoundryAutopilotRegistration.cmd"), result.LauncherPath);
        Assert.Equal(Path.Combine(registrationRoot, "Start-FoundryAutopilotRegistrationAutoLaunch.cmd"), result.AutomaticLauncherPath);
        Assert.Equal(Path.Combine(registrationRoot, "Register-FoundryAutopilotRegistrationTask.ps1"), result.AutomaticLaunchScriptPath);
        Assert.Equal(Path.Combine(registrationRoot, "config.json"), result.ConfigPath);
        Assert.Equal(Path.Combine(windowsRoot, "Windows", "Setup", "Scripts", "SetupComplete.cmd"), result.SetupCompletePath);
        Assert.Equal(logRoot, result.LogRootPath);
        Assert.True(File.Exists(result.ScriptPath));
        Assert.True(File.Exists(result.LauncherPath));
        Assert.True(File.Exists(result.AutomaticLauncherPath));
        Assert.True(File.Exists(result.AutomaticLaunchScriptPath));
        Assert.True(File.Exists(result.ConfigPath));
        Assert.True(File.Exists(result.SetupCompletePath));
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
        Assert.Equal("FoundryAutopilotInteractiveRegistration", config.RootElement.GetProperty("automaticLaunchTaskName").GetString());
        Assert.Contains(
            "DeviceManagementServiceConfig.ReadWrite.All",
            config.RootElement.GetProperty("scopes").EnumerateArray().Select(scope => scope.GetString()));
        Assert.False(config.RootElement.TryGetProperty("accessToken", out _));
        Assert.False(config.RootElement.TryGetProperty("refreshToken", out _));
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
    public void Provision_WritesAutomaticLauncherTaskRegistrationAndSetupCompleteBlock()
    {
        string windowsRoot = CreateWindowsRoot();
        var service = CreateService();

        AutopilotInteractiveRegistrationProvisioningResult result = service.Provision(windowsRoot);

        string automaticLauncher = File.ReadAllText(result.AutomaticLauncherPath);
        Assert.Contains("FoundryAutopilotInteractiveRegistration", automaticLauncher);
        Assert.Contains("schtasks.exe", automaticLauncher);
        Assert.Contains("/Delete", automaticLauncher);
        Assert.Contains("registration-result.json", automaticLauncher);
        Assert.Contains("Start-FoundryAutopilotRegistration.cmd", automaticLauncher);
        Assert.Contains("start \"\"", automaticLauncher);

        string automaticLaunchScript = File.ReadAllText(result.AutomaticLaunchScriptPath);
        Assert.Contains("FoundryAutopilotInteractiveRegistration", automaticLaunchScript);
        Assert.Contains("New-ScheduledTaskAction", automaticLaunchScript);
        Assert.Contains("New-ScheduledTaskTrigger -Once -At (Get-Date).AddSeconds(5)", automaticLaunchScript);
        Assert.Contains("-Trigger $launchTrigger", automaticLaunchScript);
        Assert.DoesNotContain("New-ScheduledTaskTrigger -AtLogon", automaticLaunchScript);
        Assert.DoesNotContain("fallbackTrigger", automaticLaunchScript);
        Assert.DoesNotContain("logonTrigger", automaticLaunchScript);
        Assert.Contains("[System.Security.Principal.WindowsIdentity]::GetCurrent().Name", automaticLaunchScript);
        Assert.Contains("New-ScheduledTaskPrincipal -UserId $interactiveUser -LogonType Interactive -RunLevel Highest", automaticLaunchScript);
        Assert.DoesNotContain("S-1-5-32-544", automaticLaunchScript);
        Assert.DoesNotContain("-GroupId", automaticLaunchScript);
        Assert.Contains("Register-ScheduledTask", automaticLaunchScript);
        Assert.DoesNotContain("Start-ScheduledTask", automaticLaunchScript);
        Assert.Contains("Start-FoundryAutopilotRegistrationAutoLaunch.cmd", automaticLaunchScript);

        string setupComplete = File.ReadAllText(result.SetupCompletePath);
        Assert.Contains("REM >>> FOUNDRY AUTOPILOT REGISTRATION BEGIN", setupComplete);
        Assert.Contains(
            "powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"%SystemRoot%\\Temp\\Foundry\\AutopilotRegistration\\Register-FoundryAutopilotRegistrationTask.ps1\" >>\"%SystemRoot%\\Temp\\Foundry\\Logs\\AutopilotRegistration\\SetupComplete.log\" 2>&1",
            setupComplete);
        Assert.Contains("REM <<< FOUNDRY AUTOPILOT REGISTRATION END", setupComplete);
    }

    [Fact]
    public void Provision_DoesNotDuplicateSetupCompleteAutomaticLaunchBlock()
    {
        string windowsRoot = CreateWindowsRoot();
        var service = CreateService();

        service.Provision(windowsRoot);
        AutopilotInteractiveRegistrationProvisioningResult result = service.Provision(windowsRoot);

        string setupComplete = File.ReadAllText(result.SetupCompletePath);

        Assert.Equal(1, CountOccurrences(setupComplete, "REM >>> FOUNDRY AUTOPILOT REGISTRATION BEGIN"));
    }

    [Fact]
    public void Provision_MovesSetupCompleteAutomaticLaunchBlockToEnd()
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

        AutopilotInteractiveRegistrationProvisioningResult result = service.Provision(windowsRoot);

        string setupComplete = File.ReadAllText(result.SetupCompletePath);

        Assert.DoesNotContain("old-autopilot-registration-command", setupComplete);
        Assert.True(
            setupComplete.IndexOf("echo existing customization", StringComparison.Ordinal) <
            setupComplete.IndexOf("REM >>> FOUNDRY AUTOPILOT REGISTRATION BEGIN", StringComparison.Ordinal));
        Assert.Equal(1, CountOccurrences(setupComplete, "REM >>> FOUNDRY AUTOPILOT REGISTRATION BEGIN"));
    }

    [Fact]
    public void Provision_StagedScriptContainsExpectedFlowWithoutExternalModuleDependencies()
    {
        string windowsRoot = CreateWindowsRoot();
        var service = CreateService();

        AutopilotInteractiveRegistrationProvisioningResult result = service.Provision(windowsRoot);

        string script = File.ReadAllText(result.ScriptPath);
        Assert.Contains("Request-DeviceCode", script);
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
        Assert.Contains("deviceManagement/importedWindowsAutopilotDeviceIdentities/import", script);
        Assert.Contains("deviceManagement/windowsAutopilotDeviceIdentities", script);
        Assert.DoesNotContain("deviceManagement/windowsAutopilotDeviceIdentities?$select=groupTag", script);
        Assert.Contains("updateDeviceProperties", script);
        Assert.Contains("AlreadyAssigned", script);
        Assert.Contains("AlreadyExists", script);
        Assert.DoesNotContain("Install-Module", script);
        Assert.DoesNotContain("Connect-MgGraph", script);
        Assert.DoesNotContain("Get-WindowsAutopilotInfo", script);
        Assert.DoesNotContain("WindowsAutopilotIntune", script);
        Assert.DoesNotContain("BackgroundWorker", script);
        Assert.DoesNotContain("Wait-AutopilotDeviceReadiness", script);
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
        Assert.Contains("Unregister-ScheduledTask", script);
        Assert.Contains("shutdown.exe", script);
        Assert.Contains("Width=\"420\"", script);
        Assert.Contains("Height=\"560\"", script);
        Assert.Contains("ResizeMode=\"NoResize\"", script);
        Assert.Contains("FontSize=\"16\"", script);
        Assert.Contains("FontSize=\"32\"", script);
        Assert.Contains("MinWidth=\"140\"", script);
        Assert.Contains("MinHeight=\"32\"", script);
        Assert.DoesNotContain("AuthenticateButton", script);
        Assert.DoesNotContain("DeviceCodeTextBox", script);
        Assert.DoesNotContain("UploadStatusTextBox", script);
        Assert.DoesNotContain("x:Name=\"StatusTextBlock\"", script);
        Assert.DoesNotContain("Content=\"Authenticate\"", script);
        Assert.DoesNotContain("Text=\"1. Authenticate\"", script);
        Assert.DoesNotContain("Text=\"2. Group tag and upload\"", script);
        Assert.DoesNotContain("CloseButton", script);
        Assert.DoesNotContain("Content=\"Close\"", script);
        Assert.DoesNotContain("Read-Host", script);
        Assert.DoesNotContain("Write-Host", script);
        Assert.DoesNotContain("Foreground=\"#", script);
        Assert.DoesNotContain("Background=\"#", script);
        Assert.DoesNotContain("FontFamily=\"", script);
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
