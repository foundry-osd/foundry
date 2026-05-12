using System.IO;
using System.Text.Json;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.Deployment.PreOobe;

namespace Foundry.Deploy.Tests;

public sealed class PreOobeScriptProvisioningServiceTests
{
    private const string DriverResourceName = "Foundry.Deploy.PreOobe.InstallDriverPack";

    [Fact]
    public void Provision_OrdersScriptsByPriorityThenId()
    {
        string windowsRoot = CreateWindowsRoot();
        var service = new PreOobeScriptProvisioningService(new SetupCompleteScriptService());

        PreOobeScriptProvisioningResult result = service.Provision(
            windowsRoot,
            [
                CreateScript("cleanup", "Cleanup-PreOobe.ps1", PreOobeScriptPriority.Cleanup),
                CreateScript("apply-branding", "Apply-Branding.ps1", PreOobeScriptPriority.Customization),
                CreateScript("driver-pack", "Install-DriverPack.ps1", PreOobeScriptPriority.DriverProvisioning)
            ]);

        string runner = File.ReadAllText(result.RunnerPath);

        Assert.True(
            runner.IndexOf("Install-DriverPack.ps1", StringComparison.Ordinal) <
            runner.IndexOf("Apply-Branding.ps1", StringComparison.Ordinal));
        Assert.True(
            runner.IndexOf("Apply-Branding.ps1", StringComparison.Ordinal) <
            runner.IndexOf("Cleanup-PreOobe.ps1", StringComparison.Ordinal));
    }

    [Fact]
    public void Provision_OrdersSamePriorityScriptsById()
    {
        string windowsRoot = CreateWindowsRoot();
        var service = new PreOobeScriptProvisioningService(new SetupCompleteScriptService());

        PreOobeScriptProvisioningResult result = service.Provision(
            windowsRoot,
            [
                CreateScript("configure-start-menu", "Configure-StartMenu.ps1", PreOobeScriptPriority.Customization),
                CreateScript("apply-branding", "Apply-Branding.ps1", PreOobeScriptPriority.Customization)
            ]);

        string runner = File.ReadAllText(result.RunnerPath);

        Assert.True(
            runner.IndexOf("Apply-Branding.ps1", StringComparison.Ordinal) <
            runner.IndexOf("Configure-StartMenu.ps1", StringComparison.Ordinal));
    }

    [Fact]
    public void Provision_ReplacesDuplicateScriptIdsWithLastDefinition()
    {
        string windowsRoot = CreateWindowsRoot();
        var service = new PreOobeScriptProvisioningService(new SetupCompleteScriptService());

        PreOobeScriptProvisioningResult result = service.Provision(
            windowsRoot,
            [
                CreateScript("apply-branding", "Apply-Branding.ps1", PreOobeScriptPriority.Customization, "-Mode", "old"),
                CreateScript("apply-branding", "Apply-Branding.ps1", PreOobeScriptPriority.Customization, "-Mode", "new")
            ]);

        using FileStream manifestStream = File.OpenRead(result.ManifestPath);
        using JsonDocument manifest = JsonDocument.Parse(manifestStream);
        JsonElement scripts = manifest.RootElement.GetProperty("scripts");

        JsonElement script = Assert.Single(scripts.EnumerateArray());
        Assert.Equal("apply-branding", script.GetProperty("id").GetString());
        string[] arguments = script.GetProperty("arguments")
            .EnumerateArray()
            .Select(argument => argument.GetString() ?? string.Empty)
            .ToArray();
        Assert.Equal(["-Mode", "new"], arguments);
    }

    [Fact]
    public void Provision_WritesSetupCompleteLauncherBlock()
    {
        string windowsRoot = CreateWindowsRoot();
        var service = new PreOobeScriptProvisioningService(new SetupCompleteScriptService());

        PreOobeScriptProvisioningResult result = service.Provision(
            windowsRoot,
            [CreateScript("driver-pack", "Install-DriverPack.ps1", PreOobeScriptPriority.DriverProvisioning)]);

        string setupComplete = File.ReadAllText(result.SetupCompletePath);

        Assert.Contains("REM >>> FOUNDRY PRE-OOBE BEGIN", setupComplete);
        Assert.Contains(
            "powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"%SystemRoot%\\Temp\\Foundry\\PreOobe\\Invoke-FoundryPreOobe.ps1\"",
            setupComplete);
        Assert.Contains("REM <<< FOUNDRY PRE-OOBE END", setupComplete);
    }

    [Fact]
    public void Provision_DoesNotDuplicateSetupCompleteLauncherBlock()
    {
        string windowsRoot = CreateWindowsRoot();
        var service = new PreOobeScriptProvisioningService(new SetupCompleteScriptService());
        PreOobeScriptDefinition script = CreateScript(
            "driver-pack",
            "Install-DriverPack.ps1",
            PreOobeScriptPriority.DriverProvisioning);

        service.Provision(windowsRoot, [script]);
        PreOobeScriptProvisioningResult result = service.Provision(windowsRoot, [script]);

        string setupComplete = File.ReadAllText(result.SetupCompletePath);

        Assert.Equal(1, CountOccurrences(setupComplete, "REM >>> FOUNDRY PRE-OOBE BEGIN"));
    }

    [Fact]
    public void Provision_RemovesLegacyDriverPackSetupCompleteBlock()
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
                    "REM >>> FOUNDRY DRIVERPACK BEGIN",
                    "legacy-driver-command",
                    "REM <<< FOUNDRY DRIVERPACK END",
                    "echo keep-existing-command"
                ]));
        var service = new PreOobeScriptProvisioningService(new SetupCompleteScriptService());

        PreOobeScriptProvisioningResult result = service.Provision(
            windowsRoot,
            [CreateScript("driver-pack", "Install-DriverPack.ps1", PreOobeScriptPriority.DriverProvisioning)]);

        string setupComplete = File.ReadAllText(result.SetupCompletePath);

        Assert.DoesNotContain("FOUNDRY DRIVERPACK", setupComplete);
        Assert.DoesNotContain("legacy-driver-command", setupComplete);
        Assert.Contains("echo keep-existing-command", setupComplete);
        Assert.Contains("FOUNDRY PRE-OOBE", setupComplete);
    }

    [Fact]
    public void Provision_ThrowsWhenScriptsAreEmpty()
    {
        string windowsRoot = CreateWindowsRoot();
        var service = new PreOobeScriptProvisioningService(new SetupCompleteScriptService());

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => service.Provision(windowsRoot, []));

        Assert.Equal("At least one pre-OOBE script is required.", exception.Message);
    }

    [Fact]
    public void Provision_ThrowsWhenEmbeddedScriptResourceIsMissing()
    {
        string windowsRoot = CreateWindowsRoot();
        var service = new PreOobeScriptProvisioningService(new SetupCompleteScriptService());
        var script = new PreOobeScriptDefinition
        {
            Id = "missing",
            FileName = "Missing.ps1",
            ResourceName = "Foundry.Deploy.PreOobe.Missing",
            Priority = PreOobeScriptPriority.Customization
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => service.Provision(windowsRoot, [script]));

        Assert.Equal(
            "Embedded pre-OOBE script resource 'Foundry.Deploy.PreOobe.Missing' was not found.",
            exception.Message);
    }

    private static PreOobeScriptDefinition CreateScript(
        string id,
        string fileName,
        PreOobeScriptPriority priority,
        params string[] arguments)
    {
        return new PreOobeScriptDefinition
        {
            Id = id,
            FileName = fileName,
            ResourceName = DriverResourceName,
            Priority = priority,
            Arguments = arguments
        };
    }

    private static string CreateWindowsRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "FoundryDeployTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
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
