using System.Text.Json;
using Foundry.Deploy.Services.Autopilot;

namespace Foundry.Deploy.Tests;

public sealed class AutopilotInteractiveRegistrationProvisioningServiceTests
{
    [Fact]
    public void Provision_StagesAssistantLauncherConfigAndRuntimeFolders()
    {
        string windowsRoot = CreateWindowsRoot();
        var service = new AutopilotInteractiveRegistrationProvisioningService();

        AutopilotInteractiveRegistrationProvisioningResult result = service.Provision(windowsRoot);

        string registrationRoot = Path.Combine(windowsRoot, "Windows", "Temp", "Foundry", "AutopilotRegistration");
        string logRoot = Path.Combine(windowsRoot, "Windows", "Temp", "Foundry", "Logs", "AutopilotRegistration");
        Assert.Equal(registrationRoot, result.RegistrationRootPath);
        Assert.Equal(Path.Combine(registrationRoot, "Start-FoundryAutopilotRegistration.ps1"), result.ScriptPath);
        Assert.Equal(Path.Combine(registrationRoot, "Start-FoundryAutopilotRegistration.cmd"), result.LauncherPath);
        Assert.Equal(Path.Combine(registrationRoot, "config.json"), result.ConfigPath);
        Assert.Equal(logRoot, result.LogRootPath);
        Assert.True(File.Exists(result.ScriptPath));
        Assert.True(File.Exists(result.LauncherPath));
        Assert.True(File.Exists(result.ConfigPath));
        Assert.True(Directory.Exists(Path.Combine(registrationRoot, "State")));
        Assert.True(Directory.Exists(logRoot));
    }

    [Fact]
    public void Provision_WritesSanitizedConfigWithDeviceCodeSettings()
    {
        string windowsRoot = CreateWindowsRoot();
        var service = new AutopilotInteractiveRegistrationProvisioningService();

        AutopilotInteractiveRegistrationProvisioningResult result = service.Provision(windowsRoot);

        using JsonDocument config = JsonDocument.Parse(File.ReadAllText(result.ConfigPath));
        Assert.Equal(1, config.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("interactiveHardwareHashUpload", config.RootElement.GetProperty("provisioningMode").GetString());
        Assert.Equal("common", config.RootElement.GetProperty("tenant").GetString());
        Assert.Equal("83eb3a92-030d-49b7-881b-32a1eb3e110a", config.RootElement.GetProperty("clientId").GetString());
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
        var service = new AutopilotInteractiveRegistrationProvisioningService();

        AutopilotInteractiveRegistrationProvisioningResult result = service.Provision(windowsRoot);

        string launcher = File.ReadAllText(result.LauncherPath);
        Assert.Contains("@echo off", launcher);
        Assert.Contains("%SystemRoot%\\Temp\\Foundry\\Logs\\AutopilotRegistration", launcher);
        Assert.Contains("launcher.log", launcher);
        Assert.Contains("%SystemRoot%\\Temp\\Foundry\\AutopilotRegistration\\Start-FoundryAutopilotRegistration.ps1", launcher);
        Assert.Contains("-ConfigPath \"%SystemRoot%\\Temp\\Foundry\\AutopilotRegistration\\config.json\"", launcher);
    }

    [Fact]
    public void Provision_StagedScriptContainsExpectedFlowWithoutExternalModuleDependencies()
    {
        string windowsRoot = CreateWindowsRoot();
        var service = new AutopilotInteractiveRegistrationProvisioningService();

        AutopilotInteractiveRegistrationProvisioningResult result = service.Provision(windowsRoot);

        string script = File.ReadAllText(result.ScriptPath);
        Assert.Contains("Invoke-DeviceCodeAuthentication", script);
        Assert.Contains("Get-AutopilotHardwareIdentity", script);
        Assert.Contains("Import-AutopilotDeviceIdentity", script);
        Assert.Contains("Invoke-GraphRequest", script);
        Assert.DoesNotContain("Install-Module", script);
        Assert.DoesNotContain("Connect-MgGraph", script);
        Assert.DoesNotContain("Get-WindowsAutopilotInfo", script);
        Assert.DoesNotContain("WindowsAutopilotIntune", script);
    }

    private static string CreateWindowsRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "FoundryDeployTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
