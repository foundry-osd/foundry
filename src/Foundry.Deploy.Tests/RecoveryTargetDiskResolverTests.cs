using Foundry.Deploy.Services.Runtime;
using Foundry.Deploy.Services.System;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class RecoveryTargetDiskResolverTests
{
    [Fact]
    public async Task ResolveAsync_WhenExactlyOneFoundryRecoveryMarkerExists_ReturnsDiskNumber()
    {
        var processRunner = new DiskPartProcessRunner();
        var resolver = new RecoveryTargetDiskResolver(
            processRunner,
            NullLogger<RecoveryTargetDiskResolver>.Instance,
            path => path.Equals(@"Z:\Recovery\WindowsRE\FoundryOsRecovery.json", StringComparison.OrdinalIgnoreCase));

        int? diskNumber = await resolver.ResolveAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, diskNumber);
        Assert.Contains(processRunner.Calls, call => call.StartsWith("diskpart.exe ", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(processRunner.Calls, call => call.StartsWith("powershell.exe ", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ResolveAsync_WhenFoundryRecoveryMarkerIsMissing_ReturnsNull()
    {
        var processRunner = new DiskPartProcessRunner();
        var resolver = new RecoveryTargetDiskResolver(
            processRunner,
            NullLogger<RecoveryTargetDiskResolver>.Instance,
            _ => false);

        int? diskNumber = await resolver.ResolveAsync(TestContext.Current.CancellationToken);

        Assert.Null(diskNumber);
        Assert.DoesNotContain(processRunner.Calls, call => call.StartsWith("powershell.exe ", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class DiskPartProcessRunner : IProcessRunner
    {
        public List<string> Calls { get; } = [];

        public Task<ProcessExecutionResult> RunAsync(
            string fileName,
            string arguments,
            string workingDirectory,
            CancellationToken cancellationToken = default)
        {
            Calls.Add($"{fileName} {arguments}");
            return Task.FromResult(new ProcessExecutionResult
            {
                ExitCode = 0,
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                StandardOutput = CreateOutput(fileName, arguments)
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

        private static string CreateOutput(string fileName, string arguments)
        {
            if (!string.Equals(fileName, "diskpart.exe", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            string script = File.ReadAllText(arguments.Replace("/s ", string.Empty, StringComparison.Ordinal).Trim('"'));
            if (script.Contains("list partition", StringComparison.OrdinalIgnoreCase))
            {
                return """
                    Partition ###  Type              Size     Offset
                    -------------  ----------------  -------  -------
                    Partition 1    System             260 MB  1024 KB
                    Partition 2    Reserved            16 MB   261 MB
                    Partition 3    Recovery          5120 MB   277 MB
                    Partition 4    Primary            470 GB  5397 MB
                    """;
            }

            return """
                Disk ###  Status         Size     Free     Dyn  Gpt
                --------  -------------  -------  -------  ---  ---
                Disk 2    Online          476 GB      0 B        *
                """;
        }
    }
}
