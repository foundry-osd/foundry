// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.Deployment.PreOobe;

namespace Foundry.Deploy.Tests;

public sealed class PreOobeScriptExecutionTests
{
    [Fact]
    public async Task Runner_StopsTimedOutScriptAndRunsRemainingScripts()
    {
        using var harness = new RunnerHarness();
        PreOobeScriptDefinition script = CreateScript("first") with { TimeoutSeconds = 3, ContinueOnError = true };
        string runner = harness.Stage(script, """
            Set-Content -LiteralPath (Join-Path $env:FOUNDRY_TEST_WINDOWS_ROOT 'child.pid') -Value $PID
            Start-Sleep -Seconds 60
            Set-Content -LiteralPath (Join-Path $env:FOUNDRY_TEST_WINDOWS_ROOT 'unexpected.txt') -Value 'finished'
            """);

        (int exitCode, string output) = await harness.RunAsync(runner);

        Assert.Equal(0, exitCode);
        Assert.Contains("timed out", output);
        Assert.True(harness.Exists("next.txt"));
        Assert.True(harness.Exists("cleanup.txt"));
        Assert.False(harness.Exists("unexpected.txt"));
        int childId = int.Parse(harness.Read("child.pid").Trim());
        Assert.DoesNotContain(Process.GetProcessesByName("powershell"), process => process.Id == childId);
    }

    [Theory]
    [InlineData("exit 7", null)]
    [InlineData("throw 'private licensing details'", 10)]
    public async Task Runner_ContinuesAfterBestEffortFailureWithoutExposingError(string content, int? timeoutSeconds)
    {
        using var harness = new RunnerHarness();
        PreOobeScriptDefinition script = CreateScript("first") with { TimeoutSeconds = timeoutSeconds, ContinueOnError = true };
        string runner = harness.Stage(script, content);

        (int exitCode, string output) = await harness.RunAsync(runner);

        Assert.Equal(0, exitCode);
        Assert.Contains("failed with exit code", output);
        Assert.DoesNotContain("private licensing details", output);
        Assert.True(harness.Exists("next.txt"));
        Assert.True(harness.Exists("cleanup.txt"));
    }

    [Fact]
    public async Task Runner_PreservesDefaultFailureAndStillRunsCleanup()
    {
        using var harness = new RunnerHarness();
        string runner = harness.Stage(CreateScript("first"), "exit 7");

        (int exitCode, _) = await harness.RunAsync(runner);

        Assert.NotEqual(0, exitCode);
        Assert.False(harness.Exists("next.txt"));
        Assert.True(harness.Exists("cleanup.txt"));
    }

    [Fact]
    public async Task Runner_PreservesNamedArgumentsInBoundedExecution()
    {
        using var harness = new RunnerHarness();
        string[] values = ["folder with spaces", "O'Brien", "embedded \"quote\"", "C:\\folder with spaces\\"];
        PreOobeScriptDefinition script = CreateScript("first") with
        {
            TimeoutSeconds = 10,
            Arguments = ["-First", values[0], "-Second", values[1], "-Third", values[2], "-Fourth", values[3]]
        };
        string runner = harness.Stage(script, """
            param([string]$First, [string]$Second, [string]$Third, [string]$Fourth)
            ConvertTo-Json -InputObject @($First, $Second, $Third, $Fourth) | Set-Content -LiteralPath (Join-Path $env:FOUNDRY_TEST_WINDOWS_ROOT 'arguments.json')
            """);

        (int exitCode, string output) = await harness.RunAsync(runner);

        Assert.True(exitCode == 0, output);
        Assert.Equal(values, JsonSerializer.Deserialize<string[]>(harness.Read("arguments.json")));
    }

    private static PreOobeScriptDefinition CreateScript(string id)
    {
        return new PreOobeScriptDefinition
        {
            Id = id,
            FileName = id + ".ps1",
            ResourceName = PreOobeScriptResources.InstallDriverPack,
            Priority = PreOobeScriptPriority.Customization
        };
    }

    private sealed class RunnerHarness : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "FoundryDeployTests", "runner O'Brien " + Guid.NewGuid().ToString("N"));
        private string WindowsRoot => Path.Combine(_root, "Windows");

        public string Stage(PreOobeScriptDefinition first, string content)
        {
            var service = new PreOobeScriptProvisioningService(new SetupCompleteScriptService());
            PreOobeScriptProvisioningResult result = service.Provision(_root,
            [
                first,
                CreateScript("next"),
                CreateScript("cleanup") with { Priority = PreOobeScriptPriority.Cleanup }
            ]);
            string scriptsRoot = Path.GetDirectoryName(result.StagedScriptPaths[0])!;
            File.WriteAllText(Path.Combine(scriptsRoot, first.FileName), content);
            File.WriteAllText(Path.Combine(scriptsRoot, "next.ps1"), "Set-Content -LiteralPath (Join-Path $env:FOUNDRY_TEST_WINDOWS_ROOT 'next.txt') -Value 'ran'");
            File.WriteAllText(Path.Combine(scriptsRoot, "cleanup.ps1"), "Set-Content -LiteralPath (Join-Path $env:FOUNDRY_TEST_WINDOWS_ROOT 'cleanup.txt') -Value 'ran'");
            File.WriteAllText(result.RunnerPath, File.ReadAllText(result.RunnerPath).Replace("$env:SystemRoot", "$env:FOUNDRY_TEST_WINDOWS_ROOT", StringComparison.Ordinal));
            return result.RunnerPath;
        }

        public async Task<(int ExitCode, string Output)> RunAsync(string runner)
        {
            string powerShell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
            var startInfo = new ProcessStartInfo(powerShell)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", runner })
            {
                startInfo.ArgumentList.Add(argument);
            }
            startInfo.Environment["FOUNDRY_TEST_WINDOWS_ROOT"] = WindowsRoot;
            using Process process = Process.Start(startInfo)!;
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
                return (process.ExitCode, await output.WaitAsync(timeout.Token) + await error.WaitAsync(timeout.Token));
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
            }
        }

        public bool Exists(string name) => File.Exists(Path.Combine(WindowsRoot, name));

        public string Read(string name) => File.ReadAllText(Path.Combine(WindowsRoot, name));

        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
