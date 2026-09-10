// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using Foundry.Bootstrap.Processes;
using Serilog;
using Xunit;

namespace Foundry.Bootstrap.Tests.Processes;

public sealed class ApplicationLauncherTests
{
    [Fact]
    public async Task NonzeroChildExitCodeIsReturned()
    {
        using var logger = new LoggerConfiguration().CreateLogger();
        var launcher = new ApplicationLauncher(logger);
        var startInfo = new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec")!)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            Arguments = "/d /c exit 22"
        };

        using Process child = Process.Start(startInfo)!;
        Assert.Equal(22, await launcher.ObserveAsync(child, CancellationToken.None));
    }

    [Fact]
    public async Task CancellingObservationLeavesChildRunning()
    {
        using var logger = new LoggerConfiguration().CreateLogger();
        var launcher = new ApplicationLauncher(logger);
        using Process child = Process.Start(new ProcessStartInfo("ping.exe", "-n 30 127.0.0.1")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        })!;
        try
        {
            using var cancellation = new CancellationTokenSource();
            Task<int> wait = launcher.ObserveAsync(child, cancellation.Token);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
            Assert.False(child.HasExited);
        }
        finally
        {
            if (!child.HasExited) { child.Kill(); }
            await child.WaitForExitAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task AlreadyCancelledLaunchDoesNotStartMissingExecutable()
    {
        using var logger = new LoggerConfiguration().CreateLogger();
        var launcher = new ApplicationLauncher(logger);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => launcher.StartDeployAsync(
            "missing.exe", new Dictionary<string, string?>(), new CancellationToken(true)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{\"protocolVersions\":[2]}")]
    public async Task LegacyOrIncompatiblePayloadUsesExplicitUnverifiedHandoff(string? manifest)
    {
        string directory = Directory.CreateTempSubdirectory("FoundryStartup-").FullName;
        try
        {
            if (manifest is not null) File.WriteAllText(Path.Combine(directory, "foundry.startup.json"), manifest);
            using var logger = new LoggerConfiguration().CreateLogger();
            var warnings = new List<string>();
            var launcher = new ApplicationLauncher(logger, warning: warnings.Add, startProcess: start =>
            {
                Assert.False(start.Environment.ContainsKey("FOUNDRY_STARTUP_PROTOCOL"));
                Assert.False(start.Environment.ContainsKey("FOUNDRY_STARTUP_LAUNCH_ID"));
                Assert.False(start.Environment.ContainsKey("FOUNDRY_STARTUP_STATUS_PATH"));
                return Process.GetCurrentProcess();
            });
            ApplicationLaunchResult result = await launcher.StartDeployAsync(Path.Combine(directory, "Foundry.Deploy.exe"),
                new Dictionary<string, string?>
                {
                    ["FOUNDRY_STARTUP_PROTOCOL"] = "1",
                    ["FOUNDRY_STARTUP_LAUNCH_ID"] = "stale",
                    ["FOUNDRY_STARTUP_STATUS_PATH"] = "stale"
                }, TestContext.Current.CancellationToken);
            Assert.True(result.Succeeded);
            Assert.False(result.ReadinessConfirmed);
            Assert.Single(warnings);
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
            ApplicationLaunchResult result = await launcher.StartDeployAsync(Path.Combine(directory, "Foundry.Deploy.exe"),
                new Dictionary<string, string?>(), TestContext.Current.CancellationToken);
            Assert.False(result.Succeeded);
            Assert.Equal("capability_invalid", result.FailureCategory);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void ChildArgumentsAndEnvironmentArePassedWithoutShellExpansion()
    {
        ProcessStartInfo start = ApplicationLauncher.CreateStartInfo(@"C:\Runtime\App.exe",
            new Dictionary<string, string?> { ["FOUNDRY_DIAGNOSTIC_SESSION_ID"] = "TEST", ["TO_REMOVE"] = null },
            ["--config", @"X:\Path with spaces\config.json"]);

        Assert.False(start.UseShellExecute);
        Assert.False(start.RedirectStandardOutput);
        Assert.Equal(@"C:\Runtime", start.WorkingDirectory);
        Assert.Equal(@"X:\Path with spaces\config.json", start.ArgumentList[1]);
        Assert.Equal("TEST", start.Environment["FOUNDRY_DIAGNOSTIC_SESSION_ID"]);
        Assert.False(start.Environment.ContainsKey("TO_REMOVE"));
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
            ApplicationLaunchResult result = await launcher.StartDeployAsync(executable,
                new Dictionary<string, string?> { ["DOTNET_DISABLE_GUI_ERRORS"] = "1" }, deadline.Token);
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
    public async Task StartupFailureRecoveryWaitsForExitWithinBoundedGrace(bool exitsDuringGrace)
    {
        string directory = Directory.CreateTempSubdirectory("FoundryStartup-").FullName;
        Process? fixture = null;
        bool recovered = false;
        try
        {
            File.WriteAllText(Path.Combine(directory, "foundry.startup.json"), "{\"protocolVersions\":[1]}");
            using var logger = new LoggerConfiguration().CreateLogger();
            var launcher = new ApplicationLauncher(logger, Path.Combine(directory, "session"),
                recoverFailure: (_, _, _) =>
                {
                    Assert.True(fixture!.HasExited);
                    recovered = true;
                }, startProcess: start =>
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
                        application = "Foundry.Deploy",
                        processId = child.Id,
                        stage = "startup_failed",
                        timestampUtc = DateTimeOffset.UtcNow
                    }));
                    return child;
                });
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            ApplicationLaunchResult result = await launcher.StartDeployAsync(Path.Combine(directory, "Foundry.Deploy.exe"),
                new Dictionary<string, string?> { ["FOUNDRY_DIAGNOSTIC_SESSION_ID"] = "TEST" }, deadline.Token);
            Assert.False(result.Succeeded);
            Assert.Equal("startup_failed", result.FailureCategory);
            Assert.Equal(exitsDuringGrace, recovered);
            Assert.Equal(exitsDuringGrace, fixture!.HasExited);
            Assert.Equal(exitsDuringGrace ? 0 : (int?)null, result.ExitCode);
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NegotiatedChildAcknowledgementUsesActualLaunchIdentity(bool connect)
    {
        string directory = Directory.CreateTempSubdirectory("FoundryStartup-").FullName;
        Process? fixture = null;
        try
        {
            string application = connect ? "Foundry.Connect" : "Foundry.Deploy";
            string executable = Path.Combine(directory, application + ".exe");
            File.WriteAllText(Path.Combine(directory, "foundry.startup.json"), "{\"protocolVersions\":[1]}");
            using var logger = new LoggerConfiguration().CreateLogger();
            var launcher = new ApplicationLauncher(logger, Path.Combine(directory, "session"), startProcess: start =>
            {
                string script = $$"""
                    $status = @{
                        protocolVersion = [int]$env:FOUNDRY_STARTUP_PROTOCOL
                        sessionId = $env:FOUNDRY_DIAGNOSTIC_SESSION_ID
                        launchId = $env:FOUNDRY_STARTUP_LAUNCH_ID
                        application = '{{application}}'
                        processId = $PID
                        stage = 'ui_ready'
                        timestampUtc = [DateTime]::UtcNow.ToString('o')
                    } | ConvertTo-Json -Compress
                    [IO.File]::WriteAllText($env:FOUNDRY_STARTUP_STATUS_PATH, $status)
                    {{(connect ? "exit 0" : "Start-Sleep -Seconds 30")}}
                    """;
                start.FileName = "powershell.exe";
                start.WorkingDirectory = Path.GetTempPath();
                start.CreateNoWindow = true;
                start.ArgumentList.Clear();
                start.ArgumentList.Add("-NoProfile");
                start.ArgumentList.Add("-NonInteractive");
                start.ArgumentList.Add("-EncodedCommand");
                start.ArgumentList.Add(Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script)));
                Process? child = Process.Start(start);
                if (child is not null) { fixture = Process.GetProcessById(child.Id); }
                return child;
            });
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var environment = new Dictionary<string, string?> { ["FOUNDRY_DIAGNOSTIC_SESSION_ID"] = "TEST" };
            ApplicationLaunchResult result = connect
                ? await launcher.RunConnectAsync(executable, "missing.json", environment, deadline.Token)
                : await launcher.StartDeployAsync(executable, environment, deadline.Token);
            Assert.True(result.Succeeded);
            Assert.True(result.ReadinessConfirmed);
            Assert.Equal("ui_ready", result.LastStage);
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
}
