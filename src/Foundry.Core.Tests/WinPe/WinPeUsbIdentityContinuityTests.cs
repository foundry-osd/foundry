// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Foundry.Core.Services.WinPe;
using Foundry.Utilities.Storage;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeUsbIdentityContinuityTests
{
    private static DiskIdentity ConfirmedIdentity => new(9, "UNIQUE", "SERIAL", "Safe USB", "USB", 64000000000);

    private const string SafeDiskJson = """
        {"Number":9,"FriendlyName":"Safe USB","SerialNumber":"SERIAL","UniqueId":"UNIQUE","BusType":"USB","IsRemovable":true,"IsSystem":false,"IsBoot":false,"Size":64000000000,"IsOffline":true,"IsReadOnly":true,"PartitionStyle":"GPT"}
        """;

    [Theory]
    [InlineData("", "", 9)]
    [InlineData("SER", "UNI", 9)]
    [InlineData("SERIAL", "UNIQUE", 10)]
    public void ValidateDiskSafety_RejectsNameOnlyPartialOrRenumberedConfirmation(string serial, string uniqueId, int number)
    {
        var options = new UsbOutputOptions
        {
            TargetDiskNumber = number,
            ExpectedDiskFriendlyName = "Safe USB",
            ExpectedDiskSerialNumber = serial,
            ExpectedDiskUniqueId = uniqueId,
            ExpectedDiskBusType = "USB",
            ExpectedDiskSizeBytes = 64000000000
        };
        var disk = new WinPeUsbDiskIdentity
        {
            Number = 9,
            FriendlyName = "Safe USB",
            SerialNumber = "SERIAL",
            UniqueId = "UNIQUE",
            BusType = "USB",
            IsRemovable = true,
            Size = 64000000000
        };

        WinPeResult result = WinPeUsbMediaService.ValidateDiskSafety(options, disk);

        Assert.False(result.IsSuccess);
        Assert.Equal(WinPeErrorCodes.UsbIdentityMismatch, result.Error?.Code);
        Assert.Equal(WinPeFailureReasons.DiskValidation, result.Error?.FailureReason);
    }

    [Theory]
    [InlineData("provision")]
    [InlineData("layout")]
    [InlineData("format")]
    public async Task GeneratedScript_WhenDiskIsReplaced_RejectsBeforeAnyMutation(string boundary)
    {
        string script = await GetBoundaryScriptAsync(boundary);
        string changedDisk = SafeDiskJson.Replace("UNIQUE", "REPLACED", StringComparison.Ordinal);

        HarnessResult result = await ExecuteSafelyAsync(script, changedDisk, boundary);

        Assert.Equal(0, result.Mutations);
        Assert.Contains("FOUNDRY_DISK_IDENTITY_MISMATCH", result.Error, StringComparison.Ordinal);
    }

    public static IEnumerable<object[]> RejectedSnapshots()
    {
        foreach (string boundary in new[] { "provision", "layout", "format" })
        {
            foreach (string change in new[] { "duplicate", "missing", "renumbered", "serial", "unique", "capacity", "model", "bus", "system", "boot", "fixed", "unknown-system", "unknown-boot", "malformed-system", "malformed-boot" })
            {
                yield return [boundary, change];
            }
        }
    }

    [Theory]
    [MemberData(nameof(RejectedSnapshots))]
    public async Task GeneratedScript_WhenIdentityOrEligibilityChanges_RejectsBeforeAnyMutation(string boundary, string change)
    {
        string inventory = change switch
        {
            "duplicate" => $"[{SafeDiskJson},{SafeDiskJson.Replace("\"Number\":9", "\"Number\":10", StringComparison.Ordinal).Replace("\"BusType\":\"USB\"", "\"BusType\":\"NVMe\"", StringComparison.Ordinal)}]",
            "missing" => "[]",
            "renumbered" => SafeDiskJson.Replace("\"Number\":9", "\"Number\":10", StringComparison.Ordinal),
            "serial" => SafeDiskJson.Replace("SERIAL", "", StringComparison.Ordinal),
            "unique" => SafeDiskJson.Replace("UNIQUE", "", StringComparison.Ordinal),
            "capacity" => SafeDiskJson.Replace("64000000000", "65000000000", StringComparison.Ordinal),
            "model" => SafeDiskJson.Replace("Safe USB", "Safe USB replacement", StringComparison.Ordinal),
            "bus" => SafeDiskJson.Replace("\"BusType\":\"USB\"", "\"BusType\":\"SATA\"", StringComparison.Ordinal),
            "system" => SafeDiskJson.Replace("\"IsSystem\":false", "\"IsSystem\":true", StringComparison.Ordinal),
            "boot" => SafeDiskJson.Replace("\"IsBoot\":false", "\"IsBoot\":true", StringComparison.Ordinal),
            "fixed" => SafeDiskJson.Replace("\"IsRemovable\":true", "\"IsRemovable\":false", StringComparison.Ordinal),
            "unknown-system" => SafeDiskJson.Replace("\"IsSystem\":false,", "", StringComparison.Ordinal),
            "unknown-boot" => SafeDiskJson.Replace("\"IsBoot\":false,", "", StringComparison.Ordinal),
            "malformed-system" => SafeDiskJson.Replace("\"IsSystem\":false", "\"IsSystem\":\"false\"", StringComparison.Ordinal),
            "malformed-boot" => SafeDiskJson.Replace("\"IsBoot\":false", "\"IsBoot\":0", StringComparison.Ordinal),
            _ => throw new ArgumentOutOfRangeException(nameof(change))
        };

        HarnessResult result = await ExecuteSafelyAsync(await GetBoundaryScriptAsync(boundary), inventory, boundary);

        Assert.Equal(0, result.Mutations);
        string expectedMarker = change is "system" or "boot" or "fixed" or "unknown-system" or "unknown-boot" or "malformed-system" or "malformed-boot"
            ? WinPeUsbMediaService.UsbUnsafeTargetMarker
            : "FOUNDRY_DISK_IDENTITY_MISMATCH";
        Assert.Contains(expectedMarker, result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("provision", true)]
    [InlineData("layout", true)]
    [InlineData("format", true)]
    [InlineData("provision", false)]
    [InlineData("layout", false)]
    [InlineData("format", false)]
    public async Task GeneratedScript_WhenConfirmedDiskIsStillEligible_ReachesMutationSentinel(string boundary, bool removableKnown)
    {
        string inventory = removableKnown ? SafeDiskJson : SafeDiskJson.Replace("\"IsRemovable\":true", "\"IsRemovable\":null", StringComparison.Ordinal);

        HarnessResult result = await ExecuteSafelyAsync(await GetBoundaryScriptAsync(boundary), inventory, boundary);

        Assert.Equal(1, result.Mutations);
        Assert.Equal("SAFE_TEST_MUTATION_SENTINEL", result.Error);
    }

    [Theory]
    [InlineData("layout")]
    [InlineData("format")]
    public async Task GeneratedScript_WhenDiskChangesAfterReadOnlyInspection_RejectsAtMutationBoundary(string boundary)
    {
        HarnessResult result = await ExecuteSafelyAsync(await GetBoundaryScriptAsync(boundary), SafeDiskJson, boundary, replaceAfterInspection: true);

        Assert.Equal(0, result.Mutations);
        Assert.Contains("FOUNDRY_DISK_IDENTITY_MISMATCH", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("provision", "FOUNDRY_DISK_IDENTITY_MISMATCH", WinPeErrorCodes.UsbIdentityMismatch)]
    [InlineData("layout", "FOUNDRY_DISK_IDENTITY_MISMATCH", WinPeErrorCodes.UsbIdentityMismatch)]
    [InlineData("format", "FOUNDRY_DISK_IDENTITY_MISMATCH", WinPeErrorCodes.UsbIdentityMismatch)]
    [InlineData("provision", "FOUNDRY_USB_UNSAFE_TARGET", WinPeErrorCodes.UsbUnsafeTarget)]
    [InlineData("layout", "FOUNDRY_USB_UNSAFE_TARGET", WinPeErrorCodes.UsbUnsafeTarget)]
    [InlineData("format", "FOUNDRY_USB_UNSAFE_TARGET", WinPeErrorCodes.UsbUnsafeTarget)]
    [InlineData("layout", WinPeErrorCodes.UsbBootCapacityUnknown, WinPeErrorCodes.UsbBootCapacityUnknown)]
    [InlineData("format", WinPeErrorCodes.UsbBootCapacityUnknown, WinPeErrorCodes.UsbBootCapacityUnknown)]
    public async Task Workflow_WhenBoundaryRejectsDisk_ReturnsExpectedValidationWithoutIdentifiers(string boundary, string marker, string code)
    {
        var runner = new WorkflowRunner(boundary, "failure", marker);

        WinPeResult<WinPeUsbProvisionResult> result = await RunWorkflowAsync(boundary, runner, TestContext.Current.CancellationToken);

        Assert.Equal(code, result.Error?.Code);
        Assert.Equal(WinPeFailureKinds.Validation, result.Error?.FailureKind);
        Assert.Equal(WinPeFailureReasons.DiskValidation, result.Error?.FailureReason);
        Assert.DoesNotContain("SERIAL", result.Error?.Details ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("UNIQUE", result.Error?.Details ?? string.Empty, StringComparison.Ordinal);
        Assert.All(runner.Arguments.Skip(1), arguments =>
        {
            Assert.DoesNotContain("EncodedCommand", arguments, StringComparison.Ordinal);
            Assert.DoesNotContain("UNIQUE", arguments, StringComparison.Ordinal);
            Assert.DoesNotContain("SERIAL", arguments, StringComparison.Ordinal);
        });
        Assert.All(runner.ScriptPaths, path => Assert.False(File.Exists(path)));
    }

    public static IEnumerable<object[]> CleanupCases()
    {
        foreach (string boundary in new[] { "provision", "layout", "format" })
        {
            foreach (string outcome in new[] { "success", "failure", "cancel", "throw" })
            {
                yield return [boundary, outcome];
            }
        }
    }

    [Theory]
    [MemberData(nameof(CleanupCases))]
    public async Task Workflow_AlwaysDeletesIdentityBearingScript(string boundary, string outcome)
    {
        var runner = new WorkflowRunner(boundary, outcome, "Synthetic process failure");

        if (outcome == "cancel")
        {
            await Assert.ThrowsAsync<OperationCanceledException>(() => RunWorkflowAsync(boundary, runner, TestContext.Current.CancellationToken));
        }
        else if (outcome == "throw")
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => RunWorkflowAsync(boundary, runner, TestContext.Current.CancellationToken));
        }
        else
        {
            WinPeResult<WinPeUsbProvisionResult> result = await RunWorkflowAsync(boundary, runner, TestContext.Current.CancellationToken);
            Assert.DoesNotContain("PowerShellProvisioningScript", result.Error?.Details ?? string.Empty, StringComparison.Ordinal);
            Assert.DoesNotContain("PowerShellBootPartitionUpdateScript", result.Error?.Details ?? string.Empty, StringComparison.Ordinal);
            Assert.DoesNotContain("UNIQUE", result.Error?.Details ?? string.Empty, StringComparison.Ordinal);
        }

        Assert.NotEmpty(runner.ScriptPaths);
        Assert.All(runner.ScriptPaths, path => Assert.False(File.Exists(path)));
    }

    [Theory]
    [InlineData("provision", true)]
    [InlineData("layout", true)]
    [InlineData("provision", false)]
    [InlineData("layout", false)]
    public async Task Workflow_WhenPrimaryIdIsMissingOrDuplicatedAcrossInventory_DoesNotStartMutationScript(string boundary, bool duplicate)
    {
        var runner = new WorkflowRunner(boundary, "failure", "Unexpected mutation script")
        {
            Inventory = duplicate
                ? $"[{SafeDiskJson},{SafeDiskJson.Replace("\"Number\":9", "\"Number\":10", StringComparison.Ordinal).Replace("\"BusType\":\"USB\"", "\"BusType\":\"NVMe\"", StringComparison.Ordinal)}]"
                : "[]"
        };

        WinPeResult<WinPeUsbProvisionResult> result = await RunWorkflowAsync(boundary, runner, TestContext.Current.CancellationToken);

        Assert.Equal(WinPeErrorCodes.UsbIdentityMismatch, result.Error?.Code);
        Assert.Single(runner.Arguments);
        Assert.Empty(runner.ScriptPaths);
    }

    private static UsbOutputOptions ConfirmedOptions => new()
    {
        TargetDiskNumber = 9,
        ExpectedDiskFriendlyName = "Safe USB",
        ExpectedDiskSerialNumber = "SERIAL",
        ExpectedDiskUniqueId = "UNIQUE",
        ExpectedDiskBusType = "USB",
        ExpectedDiskSizeBytes = 64000000000
    };

    [Theory]
    [InlineData("provision")]
    [InlineData("layout")]
    [InlineData("format")]
    [InlineData("copy")]
    public async Task Workflow_WhenCancelledDuringDiskMutation_FinishesCurrentStageAndStopsBeforeNext(string boundary)
    {
        using var cancellation = new CancellationTokenSource();
        var runner = new WorkflowRunner(boundary, "success", string.Empty)
        {
            OnBoundary = token =>
            {
                cancellation.Cancel();
                Assert.False(token.CanBeCanceled);
            }
        };

        await Assert.ThrowsAsync<OperationCanceledException>(() => RunWorkflowAsync(boundary, runner, cancellation.Token));

        Assert.Equal(boundary is "format" or "copy" ? 3 : 2, runner.Arguments.Count);
        Assert.All(runner.ScriptPaths, path => Assert.False(File.Exists(path)));
    }

    [Theory]
    [InlineData("provision")]
    [InlineData("layout")]
    [InlineData("format")]
    public async Task Workflow_WhenCancellationRacesDiskFailure_PreservesFailure(string boundary)
    {
        using var cancellation = new CancellationTokenSource();
        var runner = new WorkflowRunner(boundary, "failure", "Synthetic disk failure")
        {
            OnBoundary = _ => cancellation.Cancel()
        };

        WinPeResult<WinPeUsbProvisionResult> result = await RunWorkflowAsync(boundary, runner, cancellation.Token);

        Assert.False(result.IsSuccess);
        Assert.Contains("Synthetic disk failure", result.Error?.Details);
        Assert.Equal(boundary == "format" ? 3 : 2, runner.Arguments.Count);
    }

    private static async Task<WinPeResult<WinPeUsbProvisionResult>> RunWorkflowAsync(string boundary, IWinPeProcessRunner runner, CancellationToken cancellationToken)
    {
        string mediaPath = Path.Combine(Path.GetTempPath(), $"foundry-identity-media-{Guid.NewGuid():N}");
        Directory.CreateDirectory(mediaPath);
        try
        {
            var service = new WinPeUsbMediaService(runner);
            var artifact = new WinPeBuildArtifact { WorkingDirectoryPath = mediaPath, MediaDirectoryPath = mediaPath };
            var tools = new WinPeToolPaths { PowerShellPath = "shadowed" };
            return boundary is "provision" or "copy"
                ? await service.ProvisionAndPopulateAsync(ConfirmedOptions, artifact, tools, false, cancellationToken)
                : await service.UpdateBootPartitionAsync(ConfirmedOptions, artifact, tools, false, cancellationToken);
        }
        finally
        {
            Directory.Delete(mediaPath, true);
        }
    }

    private static async Task<string> GetBoundaryScriptAsync(string boundary)
    {
        if (boundary == "provision")
        {
            return WinPeUsbMediaService.BuildPowerShellProvisioningScript(ConfirmedIdentity, UsbPartitionStyle.Gpt, UsbFormatMode.Quick);
        }

        if (boundary == "format")
        {
            return WinPeUsbMediaService.BuildPowerShellBootPartitionUpdateScript(ConfirmedIdentity, new WinPeUsbProvisionResult { BootDriveLetter = "S:", BootPartitionSizeBytes = 2147483648, BootAllocationUnitSizeBytes = 4096 }, UsbFormatMode.Quick);
        }

        var runner = new BoundaryRunner();
        var service = new WinPeUsbMediaService(runner);
        await service.UpdateBootPartitionAsync(
            ConfirmedOptions,
            new WinPeBuildArtifact { WorkingDirectoryPath = Path.GetTempPath() },
            new WinPeToolPaths { PowerShellPath = "shadowed" }, false);
        return runner.Scripts[1];
    }

    [Theory]
    [InlineData(1073741824L, 4096U)]
    [InlineData(2147483648L, 8192U)]
    public async Task UpdateScript_WhenBootGeometryChanged_RejectsBeforeFormatting(long partitionSize, uint allocationUnitSize)
    {
        string script = await GetBoundaryScriptAsync("format");
        HarnessResult result = await ExecuteSafelyAsync(script, SafeDiskJson, "format",
            bootPartitionSize: partitionSize, allocationUnitSize: allocationUnitSize);
        Assert.Equal(0, result.Mutations);
        Assert.Contains(WinPeErrorCodes.UsbBootCapacityUnknown, result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("layout")]
    [InlineData("format")]
    public async Task GeometryReadFailure_UsesCapacityValidationBeforeMutation(string boundary)
    {
        string script = await GetBoundaryScriptAsync(boundary);
        HarnessResult result = await ExecuteSafelyAsync(script, SafeDiskJson, boundary, failCapacityRead: true);
        Assert.Equal(0, result.Mutations);
        Assert.Contains(WinPeErrorCodes.UsbBootCapacityUnknown, result.Error, StringComparison.Ordinal);
    }

    private static async Task<HarnessResult> ExecuteSafelyAsync(string script, string inventoryJson, string boundary, bool replaceAfterInspection = false,
        long bootPartitionSize = 2147483648, uint allocationUnitSize = 4096, bool failCapacityRead = false)
    {
        string fixture = Convert.ToBase64String(Encoding.UTF8.GetBytes(inventoryJson));
        string harness = $$"""
            $ErrorActionPreference = 'Stop'
            Import-Module Microsoft.PowerShell.Utility
            Import-Module Microsoft.PowerShell.Management
            $PSModuleAutoLoadingPreference = 'None'
            $env:PATH = ''
            $global:mutations = 0
            $global:inventory = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{{fixture}}')) | ConvertFrom-Json
            $global:diskQueries = 0
            function Import-Module { }
            function Get-Disk {
                param($Number)
                $global:diskQueries++
                foreach ($disk in $global:inventory) {
                    if ({{(replaceAfterInspection ? "$true" : "$false")}} -and $global:diskQueries -gt 1) { $disk.UniqueId = 'REPLACED' }
                    if (-not $PSBoundParameters.ContainsKey('Number') -or $disk.Number -eq $Number) { $disk }
                }
            }
            function Get-Partition {
                [pscustomobject]@{ PartitionNumber = 1; Size = {{bootPartitionSize}}; DriveLetter = '{{(boundary == "layout" && !failCapacityRead ? "" : "S")}}'; GptType = '{c12a7328-f81f-11d2-ba4b-00a0c93ec93b}'; AccessPaths = @('S:\'); IsActive = $true; MbrType = 'FAT32' }
                [pscustomobject]@{ PartitionNumber = 2; DriveLetter = 'T'; GptType = ''; AccessPaths = @('T:\'); IsActive = $false; MbrType = 'IFS' }
            }
            function Get-Volume {
                param($DriveLetter, $Partition)
                if ({{(failCapacityRead ? "$true" : "$false")}} -and $Partition.PartitionNumber -eq 1) { throw 'Synthetic geometry read failure' }
                if ($DriveLetter -eq 'T' -or $Partition.PartitionNumber -eq 2) {
                    [pscustomobject]@{ FileSystemLabel = 'Foundry Cache'; FileSystem = 'NTFS' }
                } else {
                    [pscustomobject]@{ FileSystemLabel = 'BOOT'; FileSystem = 'FAT32'; AllocationUnitSize = {{allocationUnitSize}} }
                }
            }
            function Stop-Mutation { $global:mutations++; throw 'SAFE_TEST_MUTATION_SENTINEL' }
            foreach ($command in @('Set-Disk', 'Remove-PartitionAccessPath', 'Clear-Disk', 'Update-HostStorageCache', 'Update-Disk', 'Initialize-Disk', 'New-Partition', 'Format-Volume', 'Add-PartitionAccessPath', 'Set-Partition', 'diskpart.exe', 'format.com', 'format.exe', 'mountvol.exe', 'Set-Content', 'Remove-Item', 'Start-Sleep')) {
                Set-Item -LiteralPath "Function:$command" -Value { Stop-Mutation }
            }
            $failure = ''
            try {
                & {
            {{script}}
                } | Out-Null
            } catch { $failure = $_.Exception.Message }
            [pscustomobject]@{ Mutations = $global:mutations; Error = $failure } | ConvertTo-Json -Compress
            """;
        string path = Path.Combine(Path.GetTempPath(), $"foundry-usb-guard-{Guid.NewGuid():N}.ps1");
        try
        {
            await File.WriteAllTextAsync(path, harness, new UTF8Encoding(true));
            var startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", path })
            {
                startInfo.ArgumentList.Add(argument);
            }

            using Process process = Process.Start(startInfo)!;
            Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
            Task<string> errorTask = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch
            {
                process.Kill(entireProcessTree: true);
                throw;
            }

            string output = await outputTask;
            string error = await errorTask;
            Assert.True(process.ExitCode == 0, error + output);
            return JsonSerializer.Deserialize<HarnessResult>(output)!;
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed record HarnessResult(int Mutations, string Error);

    private sealed class WorkflowRunner(string boundary, string outcome, string failure) : IWinPeProcessRunner
    {
        public Action<CancellationToken>? OnBoundary { get; init; }
        public string Inventory { get; init; } = SafeDiskJson;
        public List<string> Arguments { get; } = [];
        public List<string> ScriptPaths { get; } = [];

        public Task<WinPeProcessExecution> RunAsync(string fileName, string arguments, string workingDirectory, CancellationToken cancellationToken, IReadOnlyDictionary<string, string>? environmentOverrides = null)
        {
            const string layout = """{"BootPartitionSizeBytes":2147483648,"BootAllocationUnitSizeBytes":4096,"DiskNumber":9,"BootDriveLetter":"S:","CacheDriveLetter":"T:"}""";
            Arguments.Add(arguments);
            int targetCall = boundary is "format" or "copy" ? 3 : 2;
            if (arguments.Contains("-File ", StringComparison.Ordinal))
            {
                string path = arguments[(arguments.IndexOf("-File ", StringComparison.Ordinal) + 6)..].Trim('"');
                Assert.True(File.Exists(path));
                Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, File.ReadAllBytes(path)[..3]);
                ScriptPaths.Add(path);
            }

            if (Arguments.Count == targetCall)
            {
                OnBoundary?.Invoke(cancellationToken);
                if (outcome == "cancel")
                {
                    throw new OperationCanceledException();
                }

                if (outcome == "throw")
                {
                    throw new InvalidOperationException("Synthetic process start failure.");
                }
            }

            bool failed = Arguments.Count > targetCall || (Arguments.Count == targetCall && outcome == "failure");
            return Task.FromResult(new WinPeProcessExecution
            {
                FileName = fileName,
                Arguments = arguments,
                ExitCode = failed ? 17 : 0,
                StandardError = failed ? failure : string.Empty,
                StandardOutput = Arguments.Count == 1 ? Inventory : Arguments.Count == 2 ? layout : string.Empty
            });
        }

        public Task<WinPeProcessExecution> RunCmdScriptAsync(string scriptPath, string scriptArguments, string workingDirectory, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<WinPeProcessExecution> RunCmdScriptDirectAsync(string scriptPath, string scriptArguments, string workingDirectory, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class BoundaryRunner : IWinPeProcessRunner
    {
        public List<string> Scripts { get; } = [];

        public Task<WinPeProcessExecution> RunAsync(string fileName, string arguments, string workingDirectory, CancellationToken cancellationToken, IReadOnlyDictionary<string, string>? environmentOverrides = null)
        {
            Scripts.Add(arguments.Contains("-File ", StringComparison.Ordinal)
                ? File.ReadAllText(arguments[(arguments.IndexOf("-File ", StringComparison.Ordinal) + 6)..].Trim('"'))
                : Encoding.Unicode.GetString(Convert.FromBase64String(arguments.Split(' ')[^1])));
            return Task.FromResult(new WinPeProcessExecution { ExitCode = Scripts.Count == 1 ? 0 : 1, StandardOutput = Scripts.Count == 1 ? SafeDiskJson : string.Empty });
        }

        public Task<WinPeProcessExecution> RunCmdScriptAsync(string scriptPath, string scriptArguments, string workingDirectory, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<WinPeProcessExecution> RunCmdScriptDirectAsync(string scriptPath, string scriptArguments, string workingDirectory, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
