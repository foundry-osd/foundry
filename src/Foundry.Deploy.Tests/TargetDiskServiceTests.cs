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
        var processRunner = new DiskPartProcessRunner(localizedOutput: false);
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
        var processRunner = new DiskPartProcessRunner(localizedOutput: false);
        var service = new TargetDiskService(processRunner, NullLogger<TargetDiskService>.Instance);

        int? diskNumber = await service.GetDiskNumberForPathAsync(@"W:\Windows", TestContext.Current.CancellationToken);

        Assert.Equal(0, diskNumber);
        Assert.Contains(processRunner.Calls, call => call.StartsWith("diskpart.exe ", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(processRunner.Calls, call => call.StartsWith("powershell.exe ", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetDisksAsync_WhenDiskPartOutputIsLocalized_ParsesDiskInventory()
    {
        var processRunner = new DiskPartProcessRunner(localizedOutput: true);
        var service = new TargetDiskService(processRunner, NullLogger<TargetDiskService>.Instance);

        IReadOnlyList<TargetDiskInfo> disks = await service.GetDisksAsync(TestContext.Current.CancellationToken);

        TargetDiskInfo disk = Assert.Single(disks);
        Assert.Equal(0, disk.DiskNumber);
        Assert.Equal("Disque Foundry NVMe", disk.FriendlyName);
        Assert.Equal("NVME123", disk.SerialNumber);
        Assert.Equal(512UL * 1024UL * 1024UL * 1024UL, disk.SizeBytes);
        Assert.True(disk.IsSelectable);
    }

    [Fact]
    public async Task GetDisksAsync_WhenDiskPartDetailIncludesBanner_UsesDiskModelAsFriendlyName()
    {
        var processRunner = new DiskPartProcessRunner(localizedOutput: false, includeBanner: true);
        var service = new TargetDiskService(processRunner, NullLogger<TargetDiskService>.Instance);

        IReadOnlyList<TargetDiskInfo> disks = await service.GetDisksAsync(TestContext.Current.CancellationToken);

        TargetDiskInfo disk = Assert.Single(disks);
        Assert.Equal("NVMe Foundry Disk", disk.FriendlyName);
    }

    [Fact]
    public async Task GetDisksAsync_WhenDiskPartSelectionTextIsUnknownLanguage_UsesDiskModelAsFriendlyName()
    {
        var processRunner = new DiskPartProcessRunner(localizedOutput: false, includeUnknownLanguageSelectionText: true);
        var service = new TargetDiskService(processRunner, NullLogger<TargetDiskService>.Instance);

        IReadOnlyList<TargetDiskInfo> disks = await service.GetDisksAsync(TestContext.Current.CancellationToken);

        TargetDiskInfo disk = Assert.Single(disks);
        Assert.Equal("NVMe Foundry Disk", disk.FriendlyName);
    }

    [Fact]
    public async Task GetDisksAsync_WhenDiskPartTypeKeyIsUnavailable_InfersBusTypeFromHardwareTokens()
    {
        var processRunner = new DiskPartProcessRunner(localizedOutput: false, omitTypeKey: true);
        var service = new TargetDiskService(processRunner, NullLogger<TargetDiskService>.Instance);

        IReadOnlyList<TargetDiskInfo> disks = await service.GetDisksAsync(TestContext.Current.CancellationToken);

        TargetDiskInfo disk = Assert.Single(disks);
        Assert.Equal("NVMe", disk.BusType);
    }

    private sealed class DiskPartProcessRunner(
        bool localizedOutput,
        bool includeBanner = false,
        bool includeUnknownLanguageSelectionText = false,
        bool omitTypeKey = false) : IProcessRunner
    {
        private readonly bool _localizedOutput = localizedOutput;
        private readonly bool _includeBanner = includeBanner;
        private readonly bool _includeUnknownLanguageSelectionText = includeUnknownLanguageSelectionText;
        private readonly bool _omitTypeKey = omitTypeKey;

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

        private ProcessExecutionResult CreateResult(string fileName, string arguments)
        {
            if (!string.Equals(fileName, "diskpart.exe", StringComparison.OrdinalIgnoreCase))
            {
                return new ProcessExecutionResult { ExitCode = 0 };
            }

            string script = File.ReadAllText(arguments.Replace("/s ", string.Empty, StringComparison.Ordinal).Trim('"'));
            string output = script.Contains("detail disk", StringComparison.OrdinalIgnoreCase)
                ? CreateDetailDiskOutput(script, _localizedOutput, _includeBanner, _includeUnknownLanguageSelectionText, _omitTypeKey)
                : script.Contains("detail volume", StringComparison.OrdinalIgnoreCase)
                    ? """
                      Disk ###  Status         Size     Free     Dyn  Gpt
                      --------  -------------  -------  -------  ---  ---
                      Disk 0    Online          512 GB      0 B        *
                      """
                    : _localizedOutput
                        ? """
                          N° disque  Statut         Taille   Libre    Dyn  GPT
                          ---------  -------------  -------  -------  ---  ---
                          Disque 0    En ligne        512 G octets      0 octets        *
                          Disque 1    En ligne         32 G octets      0 octets
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

        private static string CreateDetailDiskOutput(
            string script,
            bool localizedOutput,
            bool includeBanner,
            bool includeUnknownLanguageSelectionText,
            bool omitTypeKey)
        {
            if (script.Contains("select disk 1", StringComparison.OrdinalIgnoreCase))
            {
                string usbOutput = localizedOutput
                    ? """
                  Disque USB Foundry
                  Type   : USB
                  Statut : En ligne
                  État de lecture seule actuel : Non
                  Lecture seule  : Non
                  Disque de démarrage  : Non
                  Numéro de série : USB123
                  """
                    : """
                  USB Foundry Disk
                  Type   : USB
                  Status : Online
                  Current Read-only State : No
                  Read-only  : No
                  Boot Disk  : No
                  Serial Number : USB123
                  """;

                return WrapDetailOutput(usbOutput, includeBanner, includeUnknownLanguageSelectionText);
            }

            string output = localizedOutput
                ? """
              Disque Foundry NVMe
              Type   : NVMe
              Statut : En ligne
              État de lecture seule actuel : Non
              Lecture seule  : Non
              Disque de démarrage  : Non
              Disque système  : Non
              Numéro de série : NVME123
              """
                : """
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

            if (omitTypeKey)
            {
                output = string.Join(
                    Environment.NewLine,
                    output
                        .Split(["\r\n", "\n"], StringSplitOptions.None)
                        .Where(line => !line.TrimStart().StartsWith("Type", StringComparison.OrdinalIgnoreCase)));
            }

            return WrapDetailOutput(output, includeBanner, includeUnknownLanguageSelectionText);
        }

        private static string WrapDetailOutput(
            string detailOutput,
            bool includeBanner,
            bool includeUnknownLanguageSelectionText)
        {
            if (includeBanner)
            {
                return AddBanner(detailOutput, selectionText: "Disk 0 is now the selected disk.");
            }

            return includeUnknownLanguageSelectionText
                ? AddBanner(detailOutput, selectionText: "LOCALIZED_SELECTION_CONFIRMATION_WITHOUT_COLON")
                : detailOutput;
        }

        private static string AddBanner(string detailOutput, string selectionText)
        {
            return $"""
              Microsoft DiskPart version 10.0.26100.1

              Copyright (C) Microsoft Corporation.
              On computer: MININT-FOUND

              DISKPART> select disk 0

              {selectionText}

              DISKPART> detail disk

              {detailOutput}
              """;
        }
    }
}
