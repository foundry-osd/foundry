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
        using var lenovo = new LenovoFixture("Audio\\driver.inf");
        var processes = new RecordingProcesses(new(3010, ""));
        ActionStepOutcome first = await lenovo.ExecuteAsync(processes, 0);
        Assert.True(first.RestartRequested);
        Assert.Equal(1, first.NextSubstep);
        Assert.Single(processes.Commands);
        processes.Result = new(0, "");
        ActionStepOutcome resumed = await lenovo.ExecuteAsync(processes, 1);
        Assert.True(resumed.Succeeded);
        Assert.Null(resumed.NextSubstep);
        Assert.EndsWith(Path.Combine("System32", "pnputil.exe"), processes.Commands[^1].FileName, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(processes.Commands.Skip(1), command => command.FileName.EndsWith("driver.exe", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LenovoInstall_RunsPnputilOncePerInf()
    {
        using var lenovo = new LenovoFixture("SCCM\\pack\\Audio\\a.inf", "SCCM\\pack\\Video Card\\b.inf", "SCCM\\pack\\readme.txt");
        var processes = new RecordingProcesses(new(0, ""));
        processes.Results.Enqueue(new(0, ""));
        processes.Results.Enqueue(new(259, ""));

        ActionStepOutcome outcome = await lenovo.ExecuteAsync(processes, 1);

        Assert.True(outcome.Succeeded);
        Assert.False(outcome.RestartRequested);
        Assert.Null(outcome.NextSubstep);
        Assert.Equal(2, processes.Commands.Count);
        Assert.All(processes.Commands, command =>
            Assert.EndsWith(Path.Combine("System32", "pnputil.exe"), command.FileName, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(["/add-driver", lenovo.Driver("SCCM\\pack\\Audio\\a.inf"), "/install"], processes.Commands[0].Arguments);
        Assert.Equal(["/add-driver", lenovo.Driver("SCCM\\pack\\Video Card\\b.inf"), "/install"], processes.Commands[1].Arguments);
        string log = lenovo.ReadLog();
        Assert.Contains(Path.Combine("SCCM", "pack", "Audio", "a.inf") + ": exit 0", log);
        Assert.Contains(Path.Combine("SCCM", "pack", "Video Card", "b.inf") + ": exit 259", log);
    }

    [Fact]
    public async Task LenovoInstall_WithRebootRequired_SucceedsWithoutRestart()
    {
        using var lenovo = new LenovoFixture("a.inf");
        var processes = new RecordingProcesses(new(3010, ""));

        ActionStepOutcome outcome = await lenovo.ExecuteAsync(processes, 1);

        Assert.True(outcome.Succeeded);
        Assert.False(outcome.RestartRequested);
        Assert.Null(outcome.NextSubstep);
    }

    [Fact]
    public async Task LenovoInstall_WithFailedInf_RunsTheRestAndFails()
    {
        using var lenovo = new LenovoFixture("a.inf", "b.inf", "c.inf");
        var processes = new RecordingProcesses(new(0, ""));
        processes.Results.Enqueue(new(0, ""));
        processes.Results.Enqueue(new(5, ""));
        processes.Results.Enqueue(new(0, ""));

        ActionStepOutcome outcome = await lenovo.ExecuteAsync(processes, 1);

        Assert.Equal(3, processes.Commands.Count);
        Assert.False(outcome.Succeeded);
        Assert.False(outcome.TerminationUncertain);
        Assert.Equal(5, outcome.ExitCode);
        Assert.Equal("driver_install_incomplete", outcome.FailureCode);
        Assert.Null(outcome.NextSubstep);
    }

    [Fact]
    public async Task LenovoInstall_WithoutInf_Fails()
    {
        using var lenovo = new LenovoFixture("readme.txt");
        var processes = new RecordingProcesses(new(0, ""));

        ActionStepOutcome outcome = await lenovo.ExecuteAsync(processes, 1);

        Assert.Empty(processes.Commands);
        Assert.False(outcome.Succeeded);
        Assert.Equal("driver_package_empty", outcome.FailureCode);
        Assert.Contains("No INF file", lenovo.ReadLog());
    }

    [Fact]
    public async Task LenovoInstall_RunsPnputilFromTheRootWorkingDirectory()
    {
        using var lenovo = new LenovoFixture("SCCM\\pack\\Audio\\a.inf");
        var processes = new RecordingProcesses(new(0, ""));

        await lenovo.ExecuteAsync(processes, 1);

        // An INF folder can be too long for a process working directory, which would throw instead of failing the INF.
        Assert.Equal(lenovo.Root, Assert.Single(processes.Commands).WorkingDirectory);
    }

    [Fact]
    public async Task LenovoInstall_WritesEachInfToTheLogBeforeTheNextOne()
    {
        using var lenovo = new LenovoFixture("a.inf", "b.inf");
        string? logWhileSecondRuns = null;
        var processes = new RecordingProcesses(new(0, ""))
        {
            OnRun = command =>
            {
                if (command.Arguments[1].EndsWith("b.inf", StringComparison.Ordinal)) logWhileSecondRuns = lenovo.ReadSharedLog();
            }
        };

        await lenovo.ExecuteAsync(processes, 1);

        Assert.Contains("a.inf: exit 0", logWhileSecondRuns);
    }

    [Fact]
    public async Task LenovoInstall_StopsOnUncertainTermination()
    {
        using var lenovo = new LenovoFixture("a.inf", "b.inf");
        var processes = new RecordingProcesses(new(0, ""));
        processes.Results.Enqueue(new(null, "", TimedOut: true, TerminationUncertain: true));

        ActionStepOutcome outcome = await lenovo.ExecuteAsync(processes, 1);

        Assert.Single(processes.Commands);
        Assert.False(outcome.Succeeded);
        Assert.True(outcome.TerminationUncertain);
        Assert.Equal("execution_uncertain", outcome.FailureCode);
    }

    [Fact]
    public async Task LenovoSubstep2_IsRejected()
    {
        using var lenovo = new LenovoFixture("a.inf");
        await Assert.ThrowsAsync<InvalidDataException>(() => lenovo.ExecuteAsync(new RecordingProcesses(new(0, "")), 2));
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
        /// <summary>Outcomes returned in order before <see cref="Result"/>.</summary>
        public Queue<ProcessOutcome> Results { get; } = new();
        public Action<ProcessCommand>? OnRun { get; init; }
        public List<ProcessCommand> Commands { get; } = [];
        public Task<ProcessOutcome> RunAsync(ProcessCommand command, CancellationToken cancellationToken)
        {
            Commands.Add(command);
            OnRun?.Invoke(command);
            return Task.FromResult(Results.Count > 0 ? Results.Dequeue() : Result);
        }
    }

    /// <summary>A PostInstall root with a staged Lenovo installer and a drivers folder holding the given files.</summary>
    private sealed class LenovoFixture : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "Foundry.PostInstall.Tests", Guid.NewGuid().ToString("N"));

        public LenovoFixture(params string[] driverFiles)
        {
            Directory.CreateDirectory(Path.Combine(Root, "Payloads", "Drivers"));
            File.WriteAllText(Path.Combine(Root, "Payloads", "Drivers", "driver.exe"), "fixture");
            Directory.CreateDirectory(DriversRoot);
            foreach (string file in driverFiles)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Driver(file))!);
                File.WriteAllText(Driver(file), "fixture");
            }
        }

        public string Root => Path.Combine(directory, "Root");
        public string DriversRoot => Path.Combine(directory, "Drivers");
        public string Driver(string relativePath) => Path.Combine(DriversRoot, relativePath);
        public string ReadLog() => File.ReadAllText(LogPath);

        /// <summary>Reads the log while the action still holds it open for writing.</summary>
        public string ReadSharedLog()
        {
            using var stream = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        private string LogPath => Path.Combine(Root, "Logs", "PreOobe", "lenovo-drivers.log");

        public Task<ActionStepOutcome> ExecuteAsync(RecordingProcesses processes, int substep) =>
            new DriverAction(Root, Environment.GetFolderPath(Environment.SpecialFolder.Windows), processes, DriversRoot).ExecuteAsync(
                new PreOobeExecutionAction
                {
                    Id = "driver",
                    BuiltInKind = PreOobeBuiltInKind.Driver,
                    Parameters = JsonSerializer.SerializeToElement(new { commandKind = "LenovoExecutable", packagePath = "Payloads/Drivers/driver.exe" })
                },
                substep,
                TestContext.Current.CancellationToken);

        public void Dispose() => Directory.Delete(directory, true);
    }
}
