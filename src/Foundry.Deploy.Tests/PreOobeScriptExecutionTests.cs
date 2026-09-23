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

    [Fact]
    public async Task Runner_RecordsTerminalOutcomeAndDoesNotReplay()
    {
        using var harness = new RunnerHarness();
        string runner = harness.Stage(CreateScript("first"), "exit 0");
        (int exitCode, string output) = await harness.RunAsync(runner);
        Assert.True(exitCode == 0, output);
        using JsonDocument result = JsonDocument.Parse(harness.Read(@"Temp\Foundry\State\PreOobe\execution-result.json"));
        Assert.Equal("completed", result.RootElement.GetProperty("outcome").GetString());
        Assert.False(string.IsNullOrWhiteSpace(result.RootElement.GetProperty("attemptId").GetString()));
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(runner)!, "Scripts", "first.ps1"), "exit 9");
        (exitCode, output) = await harness.RunAsync(runner);
        Assert.True(exitCode == 0, output);
    }

    [Fact]
    public async Task Runner_WithoutCleanupScriptDisposesInputAndRecordsFailure()
    {
        using var harness = new RunnerHarness();
        var script = CreateScript("first") with
        {
            DataFiles = [new PreOobeScriptDataFile { FileName = "private.txt", Content = "secret", IsSensitive = true }]
        };
        string runner = harness.Stage(script, "exit 7", includeCleanup: false);
        (int exitCode, _) = await harness.RunAsync(runner);
        Assert.NotEqual(0, exitCode);
        Assert.False(harness.Exists(@"Temp\Foundry\Payloads\Customization\private.txt"));
        using JsonDocument result = harness.ReadResult();
        Assert.Equal("failed", result.RootElement.GetProperty("outcome").GetString());
        JsonElement input = result.RootElement.GetProperty("scripts")[0].GetProperty("inputs")[0];
        Assert.Equal("disposed", input.GetProperty("disposition").GetString());
        Assert.True(input.GetProperty("requiresRestaging").GetBoolean());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Runner_ReconcilesInterruptedDisposalWithoutReplay(bool afterDeletion)
    {
        using var harness = new RunnerHarness();
        var script = CreateScript("first") with
        {
            DataFiles = [new PreOobeScriptDataFile { FileName = "private.txt", Content = "secret", IsSensitive = true }]
        };
        string runner = harness.Stage(script, "exit 0", includeCleanup: false);
        string original = File.ReadAllText(runner).Replace("\r\n", "\n", StringComparison.Ordinal);
        string boundary = afterDeletion
            ? "    Write-FoundryResult\n    if (@($Record.inputs"
            : "    try { Write-FoundryResult }\n    finally";
        string replacement = afterDeletion
            ? "    [Diagnostics.Process]::GetCurrentProcess().Kill()\n    Write-FoundryResult\n    if (@($Record.inputs"
            : "    [Diagnostics.Process]::GetCurrentProcess().Kill()\n    try { Write-FoundryResult }\n    finally";
        Assert.Contains(boundary, original);
        File.WriteAllText(runner, original.Replace(boundary, replacement, StringComparison.Ordinal));
        (int exitCode, _) = await harness.RunAsync(runner);
        Assert.NotEqual(0, exitCode);
        Assert.Equal(!afterDeletion, harness.Exists(@"Temp\Foundry\Payloads\Customization\private.txt"));
        File.WriteAllText(runner, original);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(runner)!, "Scripts", "first.ps1"),
            "Set-Content -LiteralPath (Join-Path $env:FOUNDRY_TEST_WINDOWS_ROOT 'replayed.txt') -Value 'bad'");
        (exitCode, _) = await harness.RunAsync(runner);
        Assert.NotEqual(0, exitCode);
        Assert.False(harness.Exists("replayed.txt"));
        Assert.False(harness.Exists(@"Temp\Foundry\Payloads\Customization\private.txt"));
        using JsonDocument result = harness.ReadResult();
        Assert.Equal("interrupted", result.RootElement.GetProperty("outcome").GetString());
        Assert.True(result.RootElement.GetProperty("scripts")[0].GetProperty("inputs")[0].GetProperty("requiresRestaging").GetBoolean());
    }

    [Fact]
    public async Task Runner_RecordingFailureStillDisposesSensitiveInput()
    {
        using var harness = new RunnerHarness();
        var script = CreateScript("first") with
        {
            DataFiles = [new PreOobeScriptDataFile { FileName = "private.txt", Content = "secret", IsSensitive = true }]
        };
        string runner = harness.Stage(script, "exit 0", includeCleanup: false);
        string original = File.ReadAllText(runner).Replace("\r\n", "\n", StringComparison.Ordinal);
        File.WriteAllText(runner, original.Replace("function Write-FoundryResult {", "function Write-FoundryResult { throw 'recording unavailable'", StringComparison.Ordinal));
        (int exitCode, _) = await harness.RunAsync(runner);
        Assert.NotEqual(0, exitCode);
        Assert.False(harness.Exists(@"Temp\Foundry\Payloads\Customization\private.txt"));
    }

    [Fact]
    public async Task Runner_RecoveryRecordingFailureStillDisposesAllSensitiveInputs()
    {
        using var harness = new RunnerHarness();
        var first = CreateScript("first") with
        {
            DataFiles = [new PreOobeScriptDataFile { FileName = "first-secret.txt", Content = "secret", IsSensitive = true }]
        };
        var next = CreateScript("next") with
        {
            DataFiles = [new PreOobeScriptDataFile { FileName = "next-secret.txt", Content = "secret", IsSensitive = true }]
        };
        string runner = harness.Stage(first, "exit 0", nextScript: next);
        string original = File.ReadAllText(runner).Replace("\r\n", "\n", StringComparison.Ordinal);
        File.WriteAllText(runner, original.Replace("    try { Write-FoundryResult }\n    finally",
            "    [Diagnostics.Process]::GetCurrentProcess().Kill()\n    try { Write-FoundryResult }\n    finally", StringComparison.Ordinal));
        Assert.NotEqual(0, (await harness.RunAsync(runner)).ExitCode);
        Assert.True(harness.Exists(@"Temp\Foundry\Payloads\Customization\next-secret.txt"));

        File.WriteAllText(runner, original.Replace("function Write-FoundryResult {",
            "function Write-FoundryResult { throw 'recording unavailable'", StringComparison.Ordinal));
        Assert.NotEqual(0, (await harness.RunAsync(runner)).ExitCode);
        Assert.False(harness.Exists(@"Temp\Foundry\Payloads\Customization\first-secret.txt"));
        Assert.False(harness.Exists(@"Temp\Foundry\Payloads\Customization\next-secret.txt"));
        Assert.False(harness.Exists("next.txt"));
    }

    [Fact]
    public async Task Runner_LeasePreventsConcurrentInvocation()
    {
        using var harness = new RunnerHarness();
        string runner = harness.Stage(CreateScript("first"), "exit 0", includeCleanup: false);
        using FileStream lease = harness.AcquireLease();
        (int exitCode, _) = await harness.RunAsync(runner);
        Assert.NotEqual(0, exitCode);
        Assert.False(harness.Exists(@"Temp\Foundry\State\PreOobe\execution-result.json"));
    }

    [Fact]
    public async Task Cleanup_RemovesDriversAtItsFunctionalStageUsingIsolatedFixture()
    {
        using var harness = new RunnerHarness();
        using Stream stream = typeof(PreOobeScriptProvisioningService).Assembly.GetManifestResourceStream(PreOobeScriptResources.CleanupPreOobe)!;
        using var reader = new StreamReader(stream);
        string cleanup = reader.ReadToEnd();
        Assert.Contains("'C:\\Drivers'", cleanup);
        // Never execute the host cleanup targets in tests: both case variants become the same fixture.
        cleanup = cleanup.Replace("'C:\\DRIVERS'", "(Join-Path $env:FOUNDRY_TEST_WINDOWS_ROOT 'Drivers')", StringComparison.Ordinal)
            .Replace("'C:\\Drivers'", "(Join-Path $env:FOUNDRY_TEST_WINDOWS_ROOT 'Drivers')", StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\", cleanup);
        string runner = harness.Stage(CreateScript("first"), cleanup, includeCleanup: false);
        harness.CreateDirectory("Drivers");
        (int exitCode, string output) = await harness.RunAsync(runner);
        Assert.True(exitCode == 0, output);
        Assert.False(harness.DirectoryExists("Drivers"));
    }

    [Fact]
    public async Task Autopilot_AtomicPublicationFailurePreservesCompletedGuard()
    {
        using var harness = new RunnerHarness();
        using Stream stream = typeof(PreOobeScriptProvisioningService).Assembly.GetManifestResourceStream("Foundry.Deploy.AutopilotRegistration.Start-FoundryAutopilotRegistration.ps1")!;
        using var reader = new StreamReader(stream);
        string assistant = reader.ReadToEnd();
        int start = assistant.IndexOf("function Write-AtomicJson", StringComparison.Ordinal);
        int end = assistant.IndexOf("function Write-State", start, StringComparison.Ordinal);
        string content = assistant[start..end] + """
            $ErrorActionPreference = 'Stop'
            $guard = Join-Path $env:FOUNDRY_TEST_WINDOWS_ROOT 'guard.json'
            '{"status":"staged"}' | Write-AtomicJson -Path $guard
            '{"status":"completed"}' | Write-AtomicJson -Path $guard
            $lock = [IO.File]::Open($guard, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
            try {
                try { '{"status":"failed"}' | Write-AtomicJson -Path $guard; throw 'publication should have failed' }
                catch { if ($_.Exception.Message -eq 'publication should have failed') { throw } }
            }
            finally { $lock.Dispose() }
            if ((Get-Content -LiteralPath $guard -Raw | ConvertFrom-Json).status -ne 'completed') { throw 'guard lost' }
            if (@(Get-ChildItem -LiteralPath $env:FOUNDRY_TEST_WINDOWS_ROOT -Filter 'guard.json.*.tmp').Count -ne 0) { throw 'candidate leaked' }
            """;
        string runner = harness.Stage(CreateScript("first"), content, includeCleanup: false);
        (int exitCode, string output) = await harness.RunAsync(runner);
        Assert.True(exitCode == 0, output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Runner_RetiresOnlyOwnedLegacyFilesAfterHookRetirement(bool legacyHookRemains)
    {
        using var harness = new RunnerHarness();
        string runner = harness.Stage(CreateScript("first"), "exit 0", includeCleanup: false);
        harness.Write(@"Temp\Foundry\PreOobe\pre-oobe-manifest.json", """{"scripts":[{"fileName":"old.ps1","dataFiles":["old.json"]}]}""");
        harness.Write(@"Temp\Foundry\PreOobe\Scripts\old.ps1", "old helper");
        harness.Write(@"Temp\Foundry\PreOobe\Data\old.json", "old input");
        harness.Write(@"Temp\Foundry\PreOobe\vendor.txt", "preserve");
        if (legacyHookRemains)
        {
            harness.Write(@"Setup\Scripts\OOBE.cmd", @"call %SystemRoot%\Temp\Foundry\PreOobe\old.cmd");
        }
        (int exitCode, string output) = await harness.RunAsync(runner);
        Assert.True(exitCode == 0, output);
        Assert.Equal(legacyHookRemains, harness.Exists(@"Temp\Foundry\PreOobe\Scripts\old.ps1"));
        Assert.Equal(legacyHookRemains, harness.Exists(@"Temp\Foundry\PreOobe\Data\old.json"));
        Assert.True(harness.Exists(@"Temp\Foundry\PreOobe\vendor.txt"));
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

        public string Stage(PreOobeScriptDefinition first, string content, bool includeCleanup = true, PreOobeScriptDefinition? nextScript = null)
        {
            var service = new PreOobeScriptProvisioningService(new SetupCompleteScriptService());
            PreOobeScriptDefinition[] scripts = includeCleanup
                ? [first, nextScript ?? CreateScript("next"), CreateScript("cleanup") with { Priority = PreOobeScriptPriority.Cleanup }]
                : [first];
            PreOobeScriptProvisioningResult result = service.Provision(_root, scripts);
            string scriptsRoot = Path.GetDirectoryName(result.StagedScriptPaths[0])!;
            File.WriteAllText(Path.Combine(scriptsRoot, first.FileName), content.Replace("$env:SystemRoot", "$env:FOUNDRY_TEST_WINDOWS_ROOT", StringComparison.Ordinal));
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

        public JsonDocument ReadResult() => JsonDocument.Parse(Read(@"Temp\Foundry\State\PreOobe\execution-result.json"));

        public FileStream AcquireLease() => File.Open(Path.Combine(WindowsRoot, @"Temp\Foundry\State\PreOobe\runner.lease"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        public void Write(string name, string content)
        {
            string path = Path.Combine(WindowsRoot, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        public void CreateDirectory(string name) => Directory.CreateDirectory(Path.Combine(WindowsRoot, name));

        public bool DirectoryExists(string name) => Directory.Exists(Path.Combine(WindowsRoot, name));

        public bool Exists(string name) => File.Exists(Path.Combine(WindowsRoot, name));

        public string Read(string name) => File.ReadAllText(Path.Combine(WindowsRoot, name));

        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
