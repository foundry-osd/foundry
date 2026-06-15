using Foundry.Deploy.Services.Runtime;
using Foundry.Deploy.Services.System;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class RecoveryTargetDiskResolverTests
{
    [Fact]
    public async Task ResolveAsync_WhenExactlyOneFoundryRecoveryMarkerExists_ReturnsDiskNumber()
    {
        var processRunner = new FixedOutputProcessRunner("""{"CandidateCount":1,"DiskNumber":2}""");
        var resolver = new RecoveryTargetDiskResolver(processRunner, NullLogger<RecoveryTargetDiskResolver>.Instance);

        int? diskNumber = await resolver.ResolveAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, diskNumber);
    }

    [Theory]
    [InlineData("""{"CandidateCount":0}""")]
    [InlineData("""{"CandidateCount":2}""")]
    [InlineData("")]
    public async Task ResolveAsync_WhenFoundryRecoveryMarkerIsMissingOrAmbiguous_ReturnsNull(string output)
    {
        var processRunner = new FixedOutputProcessRunner(output);
        var resolver = new RecoveryTargetDiskResolver(processRunner, NullLogger<RecoveryTargetDiskResolver>.Instance);

        int? diskNumber = await resolver.ResolveAsync(TestContext.Current.CancellationToken);

        Assert.Null(diskNumber);
    }

    private sealed class FixedOutputProcessRunner(string standardOutput) : IProcessRunner
    {
        public Task<ProcessExecutionResult> RunAsync(
            string fileName,
            string arguments,
            string workingDirectory,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ProcessExecutionResult
            {
                ExitCode = 0,
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                StandardOutput = standardOutput
            });
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
