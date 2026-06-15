using Foundry.Deploy.Services.Runtime;
using Foundry.Deploy.Services.System;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class RecoveryTargetDiskResolverTests
{
    [Fact]
    public async Task ResolveAsync_WhenExactlyOneFoundryRecoveryMarkerExists_ReturnsDiskNumber()
    {
        var processRunner = new DiskPartProcessRunner(existingLetter: null);
        var resolver = new RecoveryTargetDiskResolver(
            processRunner,
            NullLogger<RecoveryTargetDiskResolver>.Instance,
            path => path.EndsWith(@"Recovery\WindowsRE\FoundryOsRecovery.json", StringComparison.OrdinalIgnoreCase));

        int? diskNumber = await resolver.ResolveAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, diskNumber);
        Assert.Contains(processRunner.Calls, call => call.StartsWith("diskpart.exe ", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(processRunner.Calls, call => call.StartsWith("powershell.exe ", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ResolveAsync_WhenFoundryRecoveryMarkerIsMissing_ReturnsNull()
    {
        var processRunner = new DiskPartProcessRunner(existingLetter: null);
        var resolver = new RecoveryTargetDiskResolver(
            processRunner,
            NullLogger<RecoveryTargetDiskResolver>.Instance,
            _ => false);

        int? diskNumber = await resolver.ResolveAsync(TestContext.Current.CancellationToken);

        Assert.Null(diskNumber);
        Assert.DoesNotContain(processRunner.Calls, call => call.StartsWith("powershell.exe ", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ResolveAsync_WhenRecoveryPartitionAlreadyHasDriveLetter_UsesExistingLetter()
    {
        var processRunner = new DiskPartProcessRunner(existingLetter: 'R');
        var resolver = new RecoveryTargetDiskResolver(
            processRunner,
            NullLogger<RecoveryTargetDiskResolver>.Instance,
            path => path.Equals(@"R:\Recovery\WindowsRE\FoundryOsRecovery.json", StringComparison.OrdinalIgnoreCase));

        int? diskNumber = await resolver.ResolveAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, diskNumber);
        Assert.DoesNotContain(processRunner.ScriptContents, script => script.Contains("assign letter=", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(processRunner.Calls, call => call.StartsWith("powershell.exe ", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class DiskPartProcessRunner(char? existingLetter) : IProcessRunner
    {
        public List<string> Calls { get; } = [];
        public List<string> ScriptContents { get; } = [];

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
                StandardOutput = CreateOutput(fileName, arguments, existingLetter)
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

        private string CreateOutput(string fileName, string arguments, char? existingLetter)
        {
            if (!string.Equals(fileName, "diskpart.exe", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            string script = File.ReadAllText(arguments.Replace("/s ", string.Empty, StringComparison.Ordinal).Trim('"'));
            ScriptContents.Add(script);
            if (script.Contains("detail partition", StringComparison.OrdinalIgnoreCase))
            {
                if (!script.Contains("select partition 3", StringComparison.OrdinalIgnoreCase))
                {
                    string typeGuid = script.Contains("select partition 1", StringComparison.OrdinalIgnoreCase)
                        ? "c12a7328-f81f-11d2-ba4b-00a0c93ec93b"
                        : "ebd0a0a2-b9e5-4433-87c0-68b6b72699c7";

                    return $$"""
                        Partition 1
                        Type  : {{typeGuid}}
                        """;
                }

                string volumeLine = existingLetter.HasValue
                    ? $"  Volume 3     {existingLetter.Value}   Recovery     NTFS   Partition   5120 MB  Healthy    Hidden"
                    : "  Volume 3         Recovery     NTFS   Partition   5120 MB  Healthy    Hidden";

                return $$"""
                    Partition 3
                    Type  : de94bba4-06d1-4d40-a16a-bfd50179d6ac
                    Hidden: Yes
                    Required: Yes
                    Attrib: 0X8000000000000001

                    Volume ###  Ltr  Label        Fs     Type        Size     Status     Info
                    ----------  ---  -----------  -----  ----------  -------  ---------  --------
                    {{volumeLine}}
                    """;
            }

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
