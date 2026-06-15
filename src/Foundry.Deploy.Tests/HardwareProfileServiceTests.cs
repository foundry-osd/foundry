using Foundry.Deploy.Models;
using Foundry.Deploy.Services.Hardware;
using Foundry.Deploy.Services.System;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class HardwareProfileServiceTests
{
    [Fact]
    public async Task GetCurrentAsync_WhenPowerShellIsUnavailable_ReturnsFallbackProfileWithoutStartingPowerShell()
    {
        var processRunner = new RecordingProcessRunner();
        var service = new HardwareProfileService(
            processRunner,
            NullLogger<HardwareProfileService>.Instance,
            _ => false);

        HardwareProfile profile = await service.GetCurrentAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Unknown", profile.Manufacturer);
        Assert.Empty(processRunner.Calls);
    }

    private sealed class RecordingProcessRunner : IProcessRunner
    {
        public List<string> Calls { get; } = [];

        public Task<ProcessExecutionResult> RunAsync(
            string fileName,
            string arguments,
            string workingDirectory,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(fileName);
            return Task.FromResult(new ProcessExecutionResult { ExitCode = 1 });
        }

        public Task<ProcessExecutionResult> RunAsync(
            string fileName,
            IEnumerable<string> arguments,
            string workingDirectory,
            CancellationToken cancellationToken = default)
        {
            return RunAsync(fileName, string.Join(' ', arguments), workingDirectory, cancellationToken);
        }

        public Task<ProcessExecutionResult> RunAsync(
            string fileName,
            IEnumerable<string> arguments,
            string workingDirectory,
            Action<string>? onOutputData,
            Action<string>? onErrorData,
            CancellationToken cancellationToken = default)
        {
            return RunAsync(fileName, arguments, workingDirectory, cancellationToken);
        }
    }
}
