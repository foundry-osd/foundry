// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using Foundry.WinPE.Processes;
using Foundry.WinPE.Ui;
using Serilog;
using Xunit;

namespace Foundry.WinPE.Tests.Processes;

public sealed class ApplicationLauncherTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(20)]
    [InlineData(22)]
    public async Task CompletedUiKeepsTheGuardianExecutable(int exitCode)
    {
        string directory = Directory.CreateTempSubdirectory("FoundryPayloadExit-").FullName;
        string executable = Path.Combine(directory, "Foundry.WinPE.exe");
        try
        {
            File.WriteAllText(executable, "guardian");
            WriteManifest(directory);
            using var logger = new LoggerConfiguration().CreateLogger();
            var launcher = new ApplicationLauncher(logger, Path.Combine(directory, "session"),
                startProcess: _ => Process.Start(new ProcessStartInfo(
                    Environment.GetEnvironmentVariable("ComSpec")!, $"/d /c exit {exitCode}")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                }));

            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            ApplicationLaunchResult result = await launcher.RunUiAsync(executable,
                new Dictionary<string, string?>(), null, deadline.Token);

            Assert.Equal(exitCode, result.ExitCode);
            Assert.Equal(exitCode == 0, result.Succeeded);
            // Connect and Deploy run from the read-only boot image executable, which is never released after a child exits.
            Assert.True(File.Exists(executable));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task CancellingTheUiObservationRetainsTheRunningChildPayload()
    {
        string directory = Directory.CreateTempSubdirectory("FoundryPayloadExit-").FullName;
        string executable = Path.Combine(directory, "Foundry.WinPE.exe");
        using Process child = Process.Start(new ProcessStartInfo("ping.exe", "-n 30 127.0.0.1")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        })!;
        try
        {
            File.WriteAllText(executable, "prepared payload");
            WriteManifest(directory);
            using var logger = new LoggerConfiguration().CreateLogger();
            using var cancellation = new CancellationTokenSource();
            var launcher = new ApplicationLauncher(logger, Path.Combine(directory, "session"),
                startProcess: _ =>
                {
                    Process observed = Process.GetProcessById(child.Id);
                    cancellation.Cancel();
                    return observed;
                });

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => launcher.RunUiAsync(
                executable, new Dictionary<string, string?>(), null, cancellation.Token));

            Assert.False(child.HasExited);
            Assert.True(File.Exists(executable));
        }
        finally
        {
            if (!child.HasExited) { child.Kill(); }
            await child.WaitForExitAsync(CancellationToken.None);
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AlreadyCancelledLaunchDoesNotStartMissingExecutable()
    {
        using var logger = new LoggerConfiguration().CreateLogger();
        var launcher = new ApplicationLauncher(logger);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => launcher.RunUiAsync(
            "missing.exe", new Dictionary<string, string?>(), null, new CancellationToken(true)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{\"protocolVersions\":[2]}")]
    public async Task MissingOrIncompatibleCapabilitiesAreInvalidAndNeverStartTheApplication(string? manifest)
    {
        string directory = Directory.CreateTempSubdirectory("FoundryStartup-").FullName;
        try
        {
            if (manifest is not null) File.WriteAllText(Path.Combine(directory, "foundry.startup.json"), manifest);
            using var logger = new LoggerConfiguration().CreateLogger();
            var launcher = new ApplicationLauncher(logger, startProcess: _ => throw new InvalidOperationException("Must not start"));
            ApplicationLaunchResult result = await launcher.RunUiAsync(Path.Combine(directory, "Foundry.WinPE.exe"),
                new Dictionary<string, string?>(), null, TestContext.Current.CancellationToken);
            Assert.False(result.Succeeded);
            Assert.Equal("capability_invalid", result.FailureCategory);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task MalformedCapabilitiesNeverStartTheApplication()
    {
        string directory = Directory.CreateTempSubdirectory("FoundryStartup-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(directory, "foundry.startup.json"), "invalid");
            using var logger = new LoggerConfiguration().CreateLogger();
            var launcher = new ApplicationLauncher(logger, startProcess: _ => throw new InvalidOperationException("Must not start"));
            ApplicationLaunchResult result = await launcher.RunUiAsync(Path.Combine(directory, "Foundry.WinPE.exe"),
                new Dictionary<string, string?>(), null, TestContext.Current.CancellationToken);
            Assert.False(result.Succeeded);
            Assert.Equal("capability_invalid", result.FailureCategory);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task ChildModesRunWithoutTheGuardianConsoleWindow()
    {
        string directory = Directory.CreateTempSubdirectory("FoundryLaunchConsole-").FullName;
        try
        {
            string executable = Path.Combine(directory, "Foundry.WinPE.exe");
            WriteManifest(directory);
            var starts = new List<ProcessStartInfo>();
            using var logger = new LoggerConfiguration().CreateLogger();
            var launcher = new ApplicationLauncher(logger, Path.Combine(directory, "session"), startProcess: start =>
            {
                starts.Add(start);
                return Process.Start(new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec")!, "/d /c exit 0")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            });

            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await launcher.RunUiAsync(executable, new Dictionary<string, string?>(), null, deadline.Token);

            // Sharing the startnet console would let Ctrl+C there terminate the UI process.
            ProcessStartInfo start = Assert.Single(starts);
            Assert.True(start.CreateNoWindow);
            Assert.False(start.UseShellExecute);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task ChildArgumentsAndEnvironmentArePassedWithoutShellExpansion()
    {
        string directory = Directory.CreateTempSubdirectory("FoundryLaunchArguments-").FullName;
        try
        {
            string executable = Path.Combine(directory, "Foundry.WinPE.exe");
            WriteManifest(directory);
            var starts = new List<ProcessStartInfo>();
            using var logger = new LoggerConfiguration().CreateLogger();
            var launcher = new ApplicationLauncher(logger, Path.Combine(directory, "session"), startProcess: start =>
            {
                starts.Add(start);
                return Process.Start(new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec")!, "/d /c exit 0")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            });
            var environment = new Dictionary<string, string?> { ["FOUNDRY_DIAGNOSTIC_SESSION_ID"] = "TEST", ["TO_REMOVE"] = null };

            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await launcher.RunUiAsync(executable, environment, null, deadline.Token);

            Assert.Equal([["--ui"]], starts.Select(start => start.ArgumentList.ToArray()));
            Assert.All(starts, start =>
            {
                Assert.Equal(executable, start.FileName);
                Assert.False(start.UseShellExecute);
                Assert.False(start.RedirectStandardOutput);
                Assert.Equal(directory, start.WorkingDirectory);
                Assert.Equal("TEST", start.Environment["FOUNDRY_DIAGNOSTIC_SESSION_ID"]);
                Assert.False(start.Environment.ContainsKey("TO_REMOVE"));
            });
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task MissingManagedDependencyIsObservedBeforeChildMain()
    {
        string directory = Directory.CreateTempSubdirectory("FoundryStartup-").FullName;
        try
        {
            string appHost = Path.ChangeExtension(typeof(ApplicationLauncherTests).Assembly.Location, ".exe");
            string executable = Path.Combine(directory, Path.GetFileName(appHost));
            File.Copy(appHost, executable);
            await File.WriteAllTextAsync(Path.Combine(directory, "foundry.startup.json"),
                "{\"protocolVersions\":[1]}", TestContext.Current.CancellationToken);
            using var logger = new LoggerConfiguration().CreateLogger();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var launcher = new ApplicationLauncher(logger, Path.Combine(directory, "session"));
            ApplicationLaunchResult result = await launcher.RunUiAsync(executable,
                new Dictionary<string, string?> { ["DOTNET_DISABLE_GUI_ERRORS"] = "1" }, null, deadline.Token);
            Assert.False(result.Succeeded);
            Assert.NotNull(result.ExitCode);
            Assert.NotEqual(0, result.ExitCode);
            Assert.Null(result.LastStage);
            Assert.Equal("child_exit", result.FailureCategory);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StartupFailureWaitsBrieflyForTheExitCode(bool exitsDuringGrace)
    {
        string directory = Directory.CreateTempSubdirectory("FoundryStartup-").FullName;
        Process? fixture = null;
        try
        {
            File.WriteAllText(Path.Combine(directory, "foundry.startup.json"), "{\"protocolVersions\":[1]}");
            string executable = Path.Combine(directory, "Foundry.WinPE.exe");
            File.WriteAllText(executable, "prepared payload");
            using var logger = new LoggerConfiguration().CreateLogger();
            var launcher = new ApplicationLauncher(logger, Path.Combine(directory, "session"),
                startProcess: start =>
                {
                    Process child = Process.Start(new ProcessStartInfo("ping.exe", $"-n {(exitsDuringGrace ? 2 : 30)} 127.0.0.1")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true
                    })!;
                    fixture = Process.GetProcessById(child.Id);
                    File.WriteAllText(start.Environment["FOUNDRY_STARTUP_STATUS_PATH"]!, System.Text.Json.JsonSerializer.Serialize(new
                    {
                        protocolVersion = 1,
                        sessionId = start.Environment["FOUNDRY_DIAGNOSTIC_SESSION_ID"],
                        launchId = start.Environment["FOUNDRY_STARTUP_LAUNCH_ID"],
                        application = "Foundry.WinPE",
                        processId = child.Id,
                        stage = "startup_failed",
                        timestampUtc = DateTimeOffset.UtcNow
                    }));
                    return child;
                });
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            ApplicationLaunchResult result = await launcher.RunUiAsync(Path.Combine(directory, "Foundry.WinPE.exe"),
                new Dictionary<string, string?> { ["FOUNDRY_DIAGNOSTIC_SESSION_ID"] = "TEST" }, null, deadline.Token);
            Assert.False(result.Succeeded);
            Assert.Equal("startup_failed", result.FailureCategory);
            Assert.Equal(exitsDuringGrace, fixture!.HasExited);
            Assert.Equal(exitsDuringGrace ? 0 : (int?)null, result.ExitCode);
            Assert.True(File.Exists(executable));
        }
        finally
        {
            if (fixture is not null)
            {
                using (fixture)
                {
                    if (!fixture.HasExited) { fixture.Kill(); }
                    await fixture.WaitForExitAsync(CancellationToken.None);
                }
            }
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task EachLaunchHasItsOwnIdentityStatusFileAndUiStateFile()
    {
        string directory = Directory.CreateTempSubdirectory("FoundryRelaunch-").FullName;
        try
        {
            string executable = Path.Combine(directory, "Foundry.WinPE.exe");
            WriteManifest(directory);
            var starts = new List<ProcessStartInfo>();
            using var logger = new LoggerConfiguration().CreateLogger();
            var launcher = new ApplicationLauncher(logger, Path.Combine(directory, "session"), startProcess: start =>
            {
                starts.Add(start);
                return Process.Start(new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec")!, "/d /c exit 0")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            });

            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await launcher.RunUiAsync(executable, new Dictionary<string, string?>(), null, deadline.Token);
            await launcher.RunUiAsync(executable, new Dictionary<string, string?>(), null, deadline.Token);

            Assert.Equal(2, starts.Count);
            Assert.NotEqual(starts[0].Environment["FOUNDRY_STARTUP_LAUNCH_ID"], starts[1].Environment["FOUNDRY_STARTUP_LAUNCH_ID"]);
            Assert.NotEqual(starts[0].Environment["FOUNDRY_STARTUP_STATUS_PATH"], starts[1].Environment["FOUNDRY_STARTUP_STATUS_PATH"]);
            Assert.NotEqual(starts[0].Environment["FOUNDRY_WINPE_UI_STATE_PATH"], starts[1].Environment["FOUNDRY_WINPE_UI_STATE_PATH"]);
            Assert.All(starts, start => Assert.Equal(
                Path.GetDirectoryName(start.Environment["FOUNDRY_STARTUP_STATUS_PATH"]),
                Path.GetDirectoryName(start.Environment["FOUNDRY_WINPE_UI_STATE_PATH"])));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task TheUiStateIsReadOnceTheUiProcessExitedAfterReadiness()
    {
        string directory = Directory.CreateTempSubdirectory("FoundryUiState-").FullName;
        try
        {
            string executable = Path.Combine(directory, "Foundry.WinPE.exe");
            WriteManifest(directory);
            using var logger = new LoggerConfiguration().CreateLogger();
            var launcher = new ApplicationLauncher(logger, Path.Combine(directory, "session"), startProcess: start =>
            {
                Process child = Process.Start(new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec")!, "/d /c exit 30")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                })!;
                WriteReadyStatus(start, child.Id);
                UiStateFile.Write(start.Environment["FOUNDRY_WINPE_UI_STATE_PATH"]!,
                    new UiState(UiPhase.Deploy, DestructiveStarted: true));
                return child;
            });

            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            ApplicationLaunchResult result = await launcher.RunUiAsync(executable,
                new Dictionary<string, string?> { ["FOUNDRY_DIAGNOSTIC_SESSION_ID"] = "TEST" }, null, deadline.Token);

            Assert.False(result.Succeeded);
            Assert.True(result.ReadinessConfirmed);
            Assert.Equal(30, result.ExitCode);
            Assert.Equal(new UiState(UiPhase.Deploy, DestructiveStarted: true), result.UiState);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task TheUiStateIsReportedWhileTheUiProcessRuns()
    {
        string directory = Directory.CreateTempSubdirectory("FoundryUiSupervision-").FullName;
        try
        {
            string executable = Path.Combine(directory, "Foundry.WinPE.exe");
            WriteManifest(directory);
            using var logger = new LoggerConfiguration().CreateLogger();
            var launcher = new ApplicationLauncher(logger, Path.Combine(directory, "session"), startProcess: start =>
            {
                Process child = Process.Start(new ProcessStartInfo("ping.exe", "-n 4 127.0.0.1")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                })!;
                WriteReadyStatus(start, child.Id);
                UiStateFile.Write(start.Environment["FOUNDRY_WINPE_UI_STATE_PATH"]!, new UiState(UiPhase.Deploy, DestructiveStarted: false));
                return child;
            });
            var observed = new List<UiState>();

            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            ApplicationLaunchResult result = await launcher.RunUiAsync(executable,
                new Dictionary<string, string?> { ["FOUNDRY_DIAGNOSTIC_SESSION_ID"] = "TEST" }, observed.Add, deadline.Token);

            Assert.True(result.ReadinessConfirmed);
            Assert.Contains(new UiState(UiPhase.Deploy, DestructiveStarted: false), observed);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void WriteManifest(string directory) =>
        File.WriteAllText(Path.Combine(directory, "foundry.startup.json"), "{\"protocolVersions\":[1]}");

    private static void WriteReadyStatus(ProcessStartInfo start, int processId) =>
        File.WriteAllText(start.Environment["FOUNDRY_STARTUP_STATUS_PATH"]!, System.Text.Json.JsonSerializer.Serialize(new
        {
            protocolVersion = 1,
            sessionId = start.Environment["FOUNDRY_DIAGNOSTIC_SESSION_ID"],
            launchId = start.Environment["FOUNDRY_STARTUP_LAUNCH_ID"],
            application = "Foundry.WinPE",
            processId,
            stage = "ui_ready",
            timestampUtc = DateTimeOffset.UtcNow
        }));
}
