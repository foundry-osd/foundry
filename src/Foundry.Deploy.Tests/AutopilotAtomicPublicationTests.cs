// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;

namespace Foundry.Deploy.Tests;

public sealed class AutopilotAtomicPublicationTests
{
    [Fact]
    public async Task Autopilot_AtomicPublicationFailurePreservesCompletedGuard()
    {
        using var harness = new RunnerHarness();
        using Stream stream = typeof(Foundry.Deploy.Services.Autopilot.AutopilotInteractiveRegistrationProvisioningService).Assembly.GetManifestResourceStream("Foundry.Deploy.AutopilotRegistration.Start-FoundryAutopilotRegistration.ps1")!;
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
        string runner = harness.Stage(content);
        (int exitCode, string output) = await harness.RunAsync(runner);
        Assert.True(exitCode == 0, output);
    }

    private sealed class RunnerHarness : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "FoundryDeployTests", "runner O'Brien " + Guid.NewGuid().ToString("N"));
        private string WindowsRoot => Path.Combine(_root, "Windows");

        public string Stage(string content)
        {
            Directory.CreateDirectory(WindowsRoot);
            string path = Path.Combine(_root, "atomic.ps1");
            File.WriteAllText(path, content);
            return path;
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

        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
