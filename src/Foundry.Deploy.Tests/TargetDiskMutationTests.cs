// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text;
using System.Text.Json;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.System;
using Foundry.Utilities.Processes;
using Foundry.Utilities.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class TargetDiskMutationTests
{
    [Theory]
    [InlineData("replacement")]
    [InlineData("missing")]
    [InlineData("duplicate_usb")]
    [InlineData("uid_disappeared")]
    [InlineData("serial_changed")]
    [InlineData("renumbered")]
    [InlineData("resized")]
    [InlineData("model_changed")]
    [InlineData("bus_changed")]
    [InlineData("system")]
    [InlineData("boot")]
    [InlineData("readonly")]
    [InlineData("offline")]
    [InlineData("usb")]
    [InlineData("missing_safety")]
    [InlineData("malformed_safety")]
    [InlineData("query_failed")]
    public async Task PrepareTargetDiskAsync_WhenIdentityOrSafetyChanged_DoesNotInvokeDiskPart(string scenario)
    {
        Dictionary<string, object?> disk = CreateDisk();
        Dictionary<string, object?>[] disks = [disk];
        DiskIdentity expected = CreateIdentity();
        switch (scenario)
        {
            case "replacement": disk["UniqueId"] = "REPLACEMENT"; break;
            case "missing": disks = []; break;
            case "duplicate_usb":
                Dictionary<string, object?> duplicate = CreateDisk();
                duplicate["Number"] = 2;
                duplicate["BusType"] = "USB";
                disks = [disk, duplicate];
                break;
            case "uid_disappeared": disk["UniqueId"] = ""; break;
            case "serial_changed": disk["SerialNumber"] = "REPLACEMENT"; break;
            case "renumbered": disk["Number"] = 2; break;
            case "resized": disk["Size"] = 8192; break;
            case "model_changed": disk["FriendlyName"] = "Other"; break;
            case "bus_changed": disk["BusType"] = "NVMe"; break;
            case "system": disk["IsSystem"] = true; break;
            case "boot": disk["IsBoot"] = true; break;
            case "readonly": disk["IsReadOnly"] = true; break;
            case "offline": disk["IsOffline"] = true; break;
            case "usb":
                disk["BusType"] = "USB";
                expected = expected with { BusType = "USB" };
                break;
            case "missing_safety": disk.Remove("IsSystem"); break;
            case "malformed_safety": disk["IsBoot"] = "false"; break;
        }
        using var runner = new GuardedDiskProcessRunner(disks, queryFails: scenario == "query_failed");
        var service = new WindowsDeploymentService(runner, NullLogger<WindowsDeploymentService>.Instance, new StubWindowsImageInfoReader());

        DeploymentOperationException exception = await Assert.ThrowsAsync<DeploymentOperationException>(() => service.PrepareTargetDiskAsync(
            expected, runner.WorkingDirectory, TestContext.Current.CancellationToken));

        Assert.Equal(0, runner.Mutations);
        Assert.Equal(DeploymentFailureKinds.Validation, exception.Failure.Kind);
        Assert.Equal("target_disk_identity_mismatch", exception.Failure.Code);
        Assert.DoesNotContain("UNIQUE-1", exception.ToString(), StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(runner.WorkingDirectory, "*.ps1"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrepareTargetDiskAsync_WhenIdentityIsStable_InvokesOriginalLayout(bool serialFallback)
    {
        Dictionary<string, object?> disk = CreateDisk();
        DiskIdentity expected = CreateIdentity();
        if (serialFallback)
        {
            disk["UniqueId"] = "";
            expected = expected with { UniqueId = "" };
        }
        using var runner = new GuardedDiskProcessRunner([disk]);
        var service = new WindowsDeploymentService(runner, NullLogger<WindowsDeploymentService>.Instance, new StubWindowsImageInfoReader());

        DeploymentTargetLayout layout = await service.PrepareTargetDiskAsync(
            expected, runner.WorkingDirectory, TestContext.Current.CancellationToken);

        Assert.Equal(1, runner.Mutations);
        Assert.Equal(1, layout.DiskNumber);
        string[] commands = await File.ReadAllLinesAsync(Path.Combine(runner.WorkingDirectory, "invoked-diskpart.txt"),
            TestContext.Current.CancellationToken);
        Assert.Equal(
        [
            "select disk 1", "online disk noerr", "attributes disk clear readonly noerr", "clean", "convert gpt",
            "create partition efi size=260", "format quick fs=fat32 label=System", $"assign letter={layout.SystemPartitionRoot[0]}",
            "create partition msr size=16", "create partition primary size=5120",
            "set id=\"de94bba4-06d1-4d40-a16a-bfd50179d6ac\"", "gpt attributes=0x8000000000000001",
            "format quick fs=ntfs label=Recovery", $"assign letter={layout.RecoveryPartitionLetter}",
            "create partition primary", "format quick fs=ntfs label=Windows", $"assign letter={layout.WindowsPartitionRoot[0]}"
        ], commands);
        Assert.Empty(Directory.GetFiles(runner.WorkingDirectory, "*.ps1"));
    }

    [Fact]
    public async Task PrepareTargetDiskAsync_WhenDiskPartFails_PreservesProcessFailureAndRemovesSnapshot()
    {
        using var runner = new GuardedDiskProcessRunner([CreateDisk()], diskPartExitCode: 37);
        var service = new WindowsDeploymentService(runner, NullLogger<WindowsDeploymentService>.Instance, new StubWindowsImageInfoReader());

        DeploymentProcessException exception = await Assert.ThrowsAsync<DeploymentProcessException>(() =>
            service.PrepareTargetDiskAsync(CreateIdentity(), runner.WorkingDirectory, TestContext.Current.CancellationToken));

        Assert.Equal(37, exception.ExitCode);
        Assert.Equal(1, runner.Mutations);
        Assert.Empty(Directory.GetFiles(runner.WorkingDirectory, "*.ps1"));
    }

    [Fact]
    public async Task PrepareTargetDiskAsync_WhenIdentityIsUnusable_DoesNotStartAProcess()
    {
        using var runner = new GuardedDiskProcessRunner([CreateDisk()]);
        var service = new WindowsDeploymentService(runner, NullLogger<WindowsDeploymentService>.Instance, new StubWindowsImageInfoReader());

        await Assert.ThrowsAsync<DeploymentOperationException>(() => service.PrepareTargetDiskAsync(
            CreateIdentity() with { UniqueId = "", SerialNumber = "" }, runner.WorkingDirectory, TestContext.Current.CancellationToken));

        Assert.Equal(0, runner.ProcessCount);
    }

    [Fact]
    public async Task PrepareTargetDiskAsync_WhenSnapshotCleanupFails_PreservesValidationFailure()
    {
        using var runner = new GuardedDiskProcessRunner([], lockGuard: true);
        var service = new WindowsDeploymentService(runner, NullLogger<WindowsDeploymentService>.Instance, new StubWindowsImageInfoReader());

        DeploymentOperationException exception = await Assert.ThrowsAsync<DeploymentOperationException>(() =>
            service.PrepareTargetDiskAsync(CreateIdentity(), runner.WorkingDirectory, TestContext.Current.CancellationToken));

        Assert.Equal(DeploymentFailureKinds.Validation, exception.Failure.Kind);
        Assert.Equal(0, runner.Mutations);
    }

    private static DiskIdentity CreateIdentity() => new(1, "UNIQUE-1", "ORIGINAL", "Target", "SATA", 4096);

    private static Dictionary<string, object?> CreateDisk() => new()
    {
        ["Number"] = 1,
        ["UniqueId"] = "UNIQUE-1",
        ["SerialNumber"] = "ORIGINAL",
        ["FriendlyName"] = "Target",
        ["BusType"] = "SATA",
        ["Size"] = 4096,
        ["PartitionStyle"] = "RAW",
        ["IsSystem"] = false,
        ["IsBoot"] = false,
        ["IsReadOnly"] = false,
        ["IsOffline"] = false
    };

    private sealed class GuardedDiskProcessRunner(
        Dictionary<string, object?>[] disks,
        bool queryFails = false,
        int diskPartExitCode = 0,
        bool lockGuard = false) : IProcessRunner, IDisposable
    {
        private FileStream? _guardLock;
        public string WorkingDirectory { get; } = Path.Combine(Path.GetTempPath(), $"foundry-disk-guard-é-{Guid.NewGuid():N}");
        public int Mutations { get; private set; }
        public int ProcessCount { get; private set; }

        public Task<ProcessExecutionResult> RunAsync(string fileName, string arguments, string workingDirectory,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal("diskpart.exe", fileName);
            ProcessCount++;
            Mutations++;
            return Task.FromResult(new ProcessExecutionResult { ExitCode = 0 });
        }

        public Task<ProcessExecutionResult> RunAsync(string fileName, IEnumerable<string> arguments, string workingDirectory,
            CancellationToken cancellationToken = default)
            => RunAsync(fileName, arguments, workingDirectory, null, null, cancellationToken);

        public async Task<ProcessExecutionResult> RunAsync(string fileName, IEnumerable<string> arguments, string workingDirectory,
            Action<string>? onOutputData, Action<string>? onErrorData, CancellationToken cancellationToken = default)
        {
            Assert.Equal("powershell.exe", fileName);
            ProcessCount++;
            string[] argumentList = arguments.ToArray();
            int fileIndex = Array.IndexOf(argumentList, "-File");
            Assert.True(fileIndex >= 0);
            string guardPath = argumentList[fileIndex + 1];
            if (lockGuard) _guardLock = new FileStream(guardPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            string countPath = Path.Combine(workingDirectory, "mutation-count.txt");
            string invokedPath = Path.Combine(workingDirectory, "invoked-diskpart.txt");
            string diskData = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(disks)));
            string script = $$"""
                Import-Module Microsoft.PowerShell.Utility -ErrorAction Stop
                $PSModuleAutoLoadingPreference = 'None'
                $env:PATH = ''
                $global:MutationCount = 0
                function Get-Disk {
                    if (${{queryFails}}) { throw 'Simulated inventory failure' }
                    foreach ($disk in ([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{{diskData}}')) | ConvertFrom-Json)) {
                        $disk
                    }
                }
                function diskpart.exe {
                    $global:MutationCount++
                    if ($args[0] -ne '/s') { throw 'Unexpected DiskPart arguments' }
                    [IO.File]::WriteAllText('{{invokedPath.Replace("'", "''", StringComparison.Ordinal)}}', [IO.File]::ReadAllText($args[1]))
                    $global:LASTEXITCODE = {{diskPartExitCode}}
                }
                function Clear-Disk { throw 'Unexpected storage mutation' }
                function Set-Disk { throw 'Unexpected storage mutation' }
                function Initialize-Disk { throw 'Unexpected storage mutation' }
                function New-Partition { throw 'Unexpected storage mutation' }
                function Format-Volume { throw 'Unexpected storage mutation' }
                try {
                    & '{{guardPath.Replace("'", "''", StringComparison.Ordinal)}}'
                    exit $LASTEXITCODE
                } finally {
                    [IO.File]::WriteAllText('{{countPath.Replace("'", "''", StringComparison.Ordinal)}}', [string]$global:MutationCount)
                }
                """;
            ProcessExecutionResult result = await new Foundry.Utilities.Processes.ProcessRunner().RunAsync(
                new ProcessExecutionRequest("powershell.exe",
                    ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script))],
                    workingDirectory), cancellationToken);
            Mutations = int.Parse(await File.ReadAllTextAsync(countPath, cancellationToken));
            return result;
        }

        public void Dispose()
        {
            _guardLock?.Dispose();
            if (Directory.Exists(WorkingDirectory)) Directory.Delete(WorkingDirectory, true);
        }
    }
}
