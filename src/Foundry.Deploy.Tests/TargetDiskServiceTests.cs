using Foundry.Deploy.Models;
using Foundry.Deploy.Services.Hardware;
using Foundry.Deploy.Services.System;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class TargetDiskServiceTests
{
    [Fact]
    public async Task GetDisksAsync_UsesDiskPartAndParsesDiskInventory()
    {
        var processRunner = new DiskPartProcessRunner();
        var service = new TargetDiskService(processRunner, NullLogger<TargetDiskService>.Instance);

        IReadOnlyList<TargetDiskInfo> disks = await service.GetDisksAsync(TestContext.Current.CancellationToken);

        TargetDiskInfo disk = Assert.Single(disks);
        Assert.Equal(0, disk.DiskNumber);
        Assert.Equal("NVMe Foundry Disk", disk.FriendlyName);
        Assert.Equal("NVME123", disk.SerialNumber);
        Assert.Equal("NVMe", disk.BusType);
        Assert.Equal("GPT", disk.PartitionStyle);
        Assert.Equal(512UL * 1024UL * 1024UL * 1024UL, disk.SizeBytes);
        Assert.True(disk.IsSelectable);
        Assert.Contains(processRunner.Calls, call => call.StartsWith("diskpart.exe ", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(processRunner.Calls, call => call.StartsWith("powershell.exe ", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetDiskNumberForPathAsync_UsesDiskPartDetailVolume()
    {
        var processRunner = new DiskPartProcessRunner();
        var service = new TargetDiskService(processRunner, NullLogger<TargetDiskService>.Instance);

        int? diskNumber = await service.GetDiskNumberForPathAsync(@"W:\Windows", TestContext.Current.CancellationToken);

        Assert.Equal(0, diskNumber);
        Assert.Contains(processRunner.Calls, call => call.StartsWith("diskpart.exe ", StringComparison.OrdinalIgnoreCase));
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
            return Task.FromResult(CreateResult(fileName, arguments));
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

        private static ProcessExecutionResult CreateResult(string fileName, string arguments)
        {
            if (!string.Equals(fileName, "diskpart.exe", StringComparison.OrdinalIgnoreCase))
            {
                return new ProcessExecutionResult { ExitCode = 0 };
            }

            string script = File.ReadAllText(arguments.Replace("/s ", string.Empty, StringComparison.Ordinal).Trim('"'));
            string output = script.Contains("detail disk", StringComparison.OrdinalIgnoreCase)
                ? CreateDetailDiskOutput(script)
                : script.Contains("detail volume", StringComparison.OrdinalIgnoreCase)
                    ? """
                      Disk ###  Status         Size     Free     Dyn  Gpt
                      --------  -------------  -------  -------  ---  ---
                      Disk 0    Online          512 GB      0 B        *
                      """
                    : """
                      Disk ###  Status         Size     Free     Dyn  Gpt
                      --------  -------------  -------  -------  ---  ---
                      Disk 0    Online          512 GB      0 B        *
                      Disk 1    Online           32 GB      0 B
                      """;

            return new ProcessExecutionResult
            {
                ExitCode = 0,
                FileName = fileName,
                Arguments = arguments,
                StandardOutput = output
            };
        }

        private static string CreateDetailDiskOutput(string script)
        {
            if (script.Contains("select disk 1", StringComparison.OrdinalIgnoreCase))
            {
                return """
                  USB Foundry Disk
                  Type   : USB
                  Status : Online
                  Current Read-only State : No
                  Read-only  : No
                  Boot Disk  : No
                  Serial Number : USB123
                  """;
            }

            return """
              NVMe Foundry Disk
              Disk ID: {00000000-0000-0000-0000-000000000000}
              Type   : NVMe
              Status : Online
              Path   : 0
              Target : 0
              LUN ID : 0
              Location Path : PCIROOT(0)#PCI(0100)#NVME(P00T00L00)
              Current Read-only State : No
              Read-only  : No
              Boot Disk  : No
              Pagefile Disk  : No
              Hibernation File Disk  : No
              Crashdump Disk  : No
              Clustered Disk  : No
              Serial Number : NVME123
              """;
        }
    }
}
