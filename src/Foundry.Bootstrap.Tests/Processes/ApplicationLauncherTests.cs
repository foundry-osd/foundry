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

        Assert.Equal(22, await launcher.RunAsync(startInfo, CancellationToken.None));
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
}
