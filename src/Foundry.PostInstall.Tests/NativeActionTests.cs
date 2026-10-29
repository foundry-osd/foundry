// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Foundry.Core.Models.PreOobe;
using Foundry.PostInstall.Actions;
using Foundry.PostInstall.Execution;

namespace Foundry.PostInstall.Tests;

public sealed class NativeActionTests
{
    [Fact]
    public void AppxSelection_DoesNotBroadenOrdinaryMatching()
    {
        var packages = DismInventory.Parse("""
            DisplayName : Microsoft.Copilot
            Version : 1.0.0.0
            PackageName : Microsoft.Copilot_1.0_x64_token

            DisplayName : Microsoft.CopilotExtra
            PackageName : Microsoft.CopilotExtra_1.0_x64_token
            The operation completed successfully.
            """);
        Assert.Single(DismInventory.Select(packages, ["microsoft.copilot"], false));
        Assert.Equal(2, DismInventory.Select(packages, ["Microsoft.Copilot"], true).Count);
    }

    [Theory]
    [InlineData("unexpected localized text")]
    [InlineData("DisplayName : Incomplete\nThe operation completed successfully.")]
    [InlineData("DisplayName : A\nPackageName : A\nDisplayName : B\nPackageName : A")]
    public void AppxInventory_RejectsAmbiguousOutput(string output) =>
        Assert.Throws<InvalidDataException>(() => DismInventory.Parse(output));

    [Fact]
    public async Task LenovoRestart_CheckpointsBeforeRemainingProvisioning()
    {
        string root = Path.Combine(Path.GetTempPath(), "Foundry.PostInstall.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Payloads", "Drivers"));
        File.WriteAllText(Path.Combine(root, "Payloads", "Drivers", "driver.exe"), "fixture");
        try
        {
            var processes = new RecordingProcesses(new(3010, ""));
            var action = new PreOobeExecutionAction
            {
                Id = "driver",
                BuiltInKind = PreOobeBuiltInKind.Driver,
                Parameters = JsonSerializer.SerializeToElement(new { commandKind = "LenovoExecutable", packagePath = "Payloads/Drivers/driver.exe" })
            };
            var driver = new DriverAction(root, Environment.GetFolderPath(Environment.SpecialFolder.Windows), processes);
            ActionStepOutcome first = await driver.ExecuteAsync(action, 0, TestContext.Current.CancellationToken);
            Assert.True(first.RestartRequested);
            Assert.Equal(1, first.NextSubstep);
            Assert.Single(processes.Commands);
            processes.Result = new(0, "");
            ActionStepOutcome resumed = await driver.ExecuteAsync(action, 1, TestContext.Current.CancellationToken);
            Assert.Equal(2, resumed.NextSubstep);
            Assert.EndsWith("reg.exe", processes.Commands[^1].FileName);
            Assert.DoesNotContain(processes.Commands.Skip(1), command => command.FileName.EndsWith("driver.exe", StringComparison.Ordinal));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void WifiNormalization_PreservesNamespaceAndSetsAutomaticConnection()
    {
        string input = "<WLANProfile xmlns='http://www.microsoft.com/networking/WLAN/profile/v1'><name>Corp</name><connectionType>ESS</connectionType></WLANProfile>";
        string normalized = NetworkAction.NormalizeWifi(input);
        Assert.Contains("<connectionMode>auto</connectionMode>", normalized);
        Assert.Equal("Corp", NetworkAction.GetWifiName(normalized));
    }

    [Fact]
    public async Task Network_WithSeveralWifiProfiles_ImportsLastFirstAndConnectsTheFirstConnectable()
    {
        string root = CreateNetworkRoot(
            """
            {
              "wifiProfileRelativePath": "NetworkProfiles\\wifi-profile.xml",
              "wifiProfileSource": "provisionedWifi",
              "wifiProfileConnectivityExpectation": "importOnly",
              "wifiProfiles": [
                { "relativePath": "NetworkProfiles\\wifi-profile.xml", "source": "provisionedWifi", "connectivityExpectation": "importOnly" },
                { "relativePath": "NetworkProfiles\\wifi-profile-2.xml", "source": "manualWifi", "connectivityExpectation": "preOobeConnectable" },
                { "relativePath": "NetworkProfiles\\wifi-profile-3.xml", "source": "manualWifi", "connectivityExpectation": "preOobeConnectable" }
              ],
              "certificates": []
            }
            """,
            ("wifi-profile.xml", "Corp"),
            ("wifi-profile-2.xml", "Guest"),
            ("wifi-profile-3.xml", "Home"));
        try
        {
            var processes = new RecordingProcesses(new(0, ""));

            ActionStepOutcome outcome = await ExecuteNetworkAsync(root, processes);

            Assert.True(outcome.Succeeded);
            Assert.False(outcome.HasWarnings);
            Assert.Equal(
                [
                    "wlan add profile filename=" + WifiPath(root, "wifi-profile-3.xml") + " user=all",
                    "wlan add profile filename=" + WifiPath(root, "wifi-profile-2.xml") + " user=all",
                    "wlan add profile filename=" + WifiPath(root, "wifi-profile.xml") + " user=all",
                    "wlan connect name=Guest"
                ],
                processes.Commands.Select(static command => string.Join(' ', command.Arguments)));
            Assert.Contains("<connectionMode>auto</connectionMode>", File.ReadAllText(WifiPath(root, "wifi-profile-3.xml")));
            Assert.DoesNotContain("connectionMode", File.ReadAllText(WifiPath(root, "wifi-profile.xml")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Network_WithOnlyTheSingleWifiEntry_ImportsAndConnectsIt()
    {
        string root = CreateNetworkRoot(
            """
            {
              "wifiProfileRelativePath": "NetworkProfiles\\wifi-profile.xml",
              "wifiProfileSource": "manualWifi",
              "wifiProfileConnectivityExpectation": "preOobeConnectable",
              "certificates": []
            }
            """,
            ("wifi-profile.xml", "Guest"));
        try
        {
            var processes = new RecordingProcesses(new(0, ""));

            ActionStepOutcome outcome = await ExecuteNetworkAsync(root, processes);

            Assert.True(outcome.Succeeded);
            Assert.Equal(
                ["wlan add profile filename=" + WifiPath(root, "wifi-profile.xml") + " user=all", "wlan connect name=Guest"],
                processes.Commands.Select(static command => string.Join(' ', command.Arguments)));
        }
        finally { Directory.Delete(root, true); }
    }

    private static string CreateNetworkRoot(string settings, params (string FileName, string Name)[] profiles)
    {
        string root = Path.Combine(Path.GetTempPath(), "Foundry.PostInstall.Tests", Guid.NewGuid().ToString("N"));
        string directory = Path.Combine(root, "Payloads", "NetworkProfiles");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "import-settings.json"), settings);
        foreach ((string fileName, string name) in profiles)
        {
            File.WriteAllText(
                Path.Combine(directory, fileName),
                $"<WLANProfile xmlns='http://www.microsoft.com/networking/WLAN/profile/v1'><name>{name}</name><connectionType>ESS</connectionType></WLANProfile>");
        }

        return root;
    }

    private static string WifiPath(string root, string fileName) => Path.Combine(root, "Payloads", "NetworkProfiles", fileName);

    private static Task<ActionStepOutcome> ExecuteNetworkAsync(string root, RecordingProcesses processes)
    {
        var action = new PreOobeExecutionAction
        {
            Id = "network-profile-roaming",
            BuiltInKind = PreOobeBuiltInKind.Network,
            Parameters = JsonSerializer.SerializeToElement(new { settingsPath = "Payloads/NetworkProfiles/import-settings.json" })
        };
        return new NetworkAction(root, Environment.GetFolderPath(Environment.SpecialFolder.Windows), processes, new NoCertificates())
            .ExecuteAsync(action, TestContext.Current.CancellationToken);
    }

    private sealed class NoCertificates : ICertificateImporter
    {
        public void Import(string path, string kind, string store, string? passwordPath) =>
            throw new InvalidOperationException("No certificate is expected.");
    }

    private sealed class RecordingProcesses(ProcessOutcome outcome) : IPreOobeProcessExecutor
    {
        public ProcessOutcome Result { get; set; } = outcome;
        public List<ProcessCommand> Commands { get; } = [];
        public Task<ProcessOutcome> RunAsync(ProcessCommand command, CancellationToken cancellationToken)
        { Commands.Add(command); return Task.FromResult(Result); }
    }
}
