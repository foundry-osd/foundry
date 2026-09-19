// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeUsbCapacityTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "FoundryCapacityTests", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(0L, 4096U)]
    [InlineData(2147483648L, 0U)]
    [InlineData(2147483648L, 513U)]
    public void UnknownGeometry_IsRejected(long capacity, uint allocationUnitSize)
    {
        Directory.CreateDirectory(root);
        WinPeResult result = WinPeUsbCapacityPolicy.Validate(root, (ulong)capacity, allocationUnitSize, TestContext.Current.CancellationToken);
        Assert.Equal(WinPeErrorCodes.UsbBootCapacityUnknown, result.Error?.Code);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void CapacityBoundary_IncludesFilesystemReserveAndAllocation(long extraBytes, bool fits)
    {
        Directory.CreateDirectory(root);
        const ulong capacity = 64 * 1024 * 1024;
        using (FileStream file = File.Create(Path.Combine(root, "boot.wim")))
            file.SetLength((long)capacity - (16 * 1024 * 1024) - 4096 + extraBytes);

        WinPeResult result = WinPeUsbCapacityPolicy.Validate(root, capacity, 4096, TestContext.Current.CancellationToken);

        Assert.Equal(fits, result.IsSuccess);
        if (!fits)
        {
            Assert.Equal(capacity + 4096, result.Error?.RequiredBytes);
            Assert.Equal(capacity, result.Error?.AvailableBytes);
        }
    }

    [Fact]
    public void TinyFilesAndEmptyDirectories_ConsumeAllocationUnits()
    {
        Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root, "one"), [1]);
        File.WriteAllBytes(Path.Combine(root, "two"), [2]);
        const ulong capacity = (16 * 1024 * 1024) + (3 * 4096);
        Assert.True(WinPeUsbCapacityPolicy.Validate(root, capacity, 4096, TestContext.Current.CancellationToken).IsSuccess);

        Directory.CreateDirectory(Path.Combine(root, "empty"));

        Assert.Equal(WinPeErrorCodes.UsbBootCapacityInsufficient,
            WinPeUsbCapacityPolicy.Validate(root, capacity, 4096, TestContext.Current.CancellationToken).Error?.Code);
    }

    [Fact]
    public void FileBeyondFat32Limit_IsRejectedEvenOnLargerPartition()
    {
        Directory.CreateDirectory(root);
        using (FileStream file = File.Create(Path.Combine(root, "boot.wim"))) file.SetLength(1L + uint.MaxValue);
        WinPeResult result = WinPeUsbCapacityPolicy.Validate(root, 8UL * 1024 * 1024 * 1024, 4096, TestContext.Current.CancellationToken);
        Assert.Equal(WinPeErrorCodes.UsbBootFileTooLarge, result.Error?.Code);
        Assert.Equal((ulong)uint.MaxValue, result.Error?.AvailableBytes);
    }

    [Fact]
    public void LargerExistingPartition_DoesNotInheritNewMediaTwoGiBLimit()
    {
        Directory.CreateDirectory(root);
        using (FileStream file = File.Create(Path.Combine(root, "boot.wim"))) file.SetLength(3L * 1024 * 1024 * 1024);
        Assert.True(WinPeUsbCapacityPolicy.Validate(root, 4UL * 1024 * 1024 * 1024, 8192, TestContext.Current.CancellationToken).IsSuccess);
    }

    [Fact]
    public void MissingMediaAndCancellation_DoNotPassValidation()
    {
        Directory.CreateDirectory(root);
        Assert.Equal(WinPeErrorCodes.UsbBootCapacityUnknown,
            WinPeUsbCapacityPolicy.Validate(Path.Combine(root, "missing"), 2147483648, 4096, TestContext.Current.CancellationToken).Error?.Code);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => WinPeUsbCapacityPolicy.Validate(root, 2147483648, 4096, cancellation.Token));
    }

    [Theory]
    [InlineData(false, 2147483648L, 2147483648L)]
    [InlineData(true, 536870912L, 536870912L)]
    public async Task OversizedPreparedMedia_IsRejectedBeforeFormatting(bool update, long capacity, long fileSize)
    {
        Directory.CreateDirectory(root);
        using (FileStream file = File.Create(Path.Combine(root, "boot.wim")))
        {
            file.SetLength(fileSize);
        }
        var runner = new CapacityRunner(capacity);
        var service = new WinPeUsbMediaService(runner);
        var options = new UsbOutputOptions
        {
            TargetDiskNumber = 9,
            ExpectedDiskFriendlyName = "Safe USB",
            ExpectedDiskSerialNumber = "SERIAL",
            ExpectedDiskUniqueId = "UNIQUE",
            ExpectedDiskBusType = "USB",
            ExpectedDiskSizeBytes = 64000000000
        };
        var artifact = new WinPeBuildArtifact { WorkingDirectoryPath = root, MediaDirectoryPath = root };
        var tools = new WinPeToolPaths { PowerShellPath = "shadowed" };

        WinPeResult<WinPeUsbProvisionResult> result = update
            ? await service.UpdateBootPartitionAsync(options, artifact, tools, false, TestContext.Current.CancellationToken)
            : await service.ProvisionAndPopulateAsync(options, artifact, tools, false, TestContext.Current.CancellationToken);

        Assert.Equal("WINPE_USB_BOOT_CAPACITY_INSUFFICIENT", result.Error?.Code);
        Assert.Equal(WinPeFailureKinds.Validation, result.Error?.FailureKind);
        Assert.False(runner.MutationAttempted);
    }

    [Fact]
    public async Task BootExReplacement_IsIncludedBeforeUpdateFormatting()
    {
        string media = Path.Combine(root, "media");
        Directory.CreateDirectory(Path.Combine(media, "EFI", "Boot"));
        Directory.CreateDirectory(Path.Combine(root, "bootbins"));
        File.WriteAllBytes(Path.Combine(media, "EFI", "Boot", "bootx64.efi"), [1]);
        using (FileStream file = File.Create(Path.Combine(root, "bootbins", "bootmgfw_EX.efi"))) file.SetLength(1024 * 1024);
        var runner = new CapacityRunner(18 * 1024 * 1024);
        var service = new WinPeUsbMediaService(runner);

        WinPeResult<WinPeUsbProvisionResult> result = await service.UpdateBootPartitionAsync(
            new UsbOutputOptions
            {
                TargetDiskNumber = 9,
                ExpectedDiskFriendlyName = "Safe USB",
                ExpectedDiskSerialNumber = "SERIAL",
                ExpectedDiskUniqueId = "UNIQUE",
                ExpectedDiskBusType = "USB",
                ExpectedDiskSizeBytes = 64000000000
            },
            new WinPeBuildArtifact { WorkingDirectoryPath = root, MediaDirectoryPath = media, Architecture = WinPeArchitecture.X64 },
            new WinPeToolPaths { PowerShellPath = "shadowed" }, true, TestContext.Current.CancellationToken);

        Assert.Equal(WinPeErrorCodes.UsbBootCapacityInsufficient, result.Error?.Code);
        Assert.Equal(1024 * 1024, new FileInfo(Path.Combine(media, "EFI", "Boot", "bootx64.efi")).Length);
        Assert.True(File.Exists(Path.Combine(media, "EFI", "Microsoft", "Boot", "bootmgfw.efi")));
        Assert.False(runner.MutationAttempted);
    }

    [Fact]
    public async Task MissingExistingCapacity_IsRejectedBeforeUpdateFormatting()
    {
        Directory.CreateDirectory(root);
        var runner = new CapacityRunner(0);
        var service = new WinPeUsbMediaService(runner);
        WinPeResult<WinPeUsbProvisionResult> result = await service.UpdateBootPartitionAsync(
            new UsbOutputOptions
            {
                TargetDiskNumber = 9,
                ExpectedDiskFriendlyName = "Safe USB",
                ExpectedDiskSerialNumber = "SERIAL",
                ExpectedDiskUniqueId = "UNIQUE",
                ExpectedDiskBusType = "USB",
                ExpectedDiskSizeBytes = 64000000000
            },
            new WinPeBuildArtifact { WorkingDirectoryPath = root, MediaDirectoryPath = root },
            new WinPeToolPaths { PowerShellPath = "shadowed" }, false, TestContext.Current.CancellationToken);
        Assert.Equal(WinPeErrorCodes.UsbBootCapacityUnknown, result.Error?.Code);
        Assert.False(runner.MutationAttempted);
    }

    public void Dispose() => Directory.Delete(root, true);

    private sealed class CapacityRunner(long capacity) : IWinPeProcessRunner
    {
        public bool MutationAttempted { get; private set; }
        public Task<WinPeProcessExecution> RunAsync(string fileName, string arguments, string workingDirectory, CancellationToken cancellationToken, IReadOnlyDictionary<string, string>? environmentOverrides = null)
        {
            string script = arguments.Contains("-File ", StringComparison.Ordinal)
                ? File.ReadAllText(arguments[(arguments.IndexOf("-File ", StringComparison.Ordinal) + 6)..].Trim('"')) : string.Empty;
            bool layout = script.Contains("$hasFoundryBootPartitionType", StringComparison.Ordinal);
            MutationAttempted |= script.Contains("Format-Volume", StringComparison.Ordinal);
            string output = layout
                ? $$"""{"BootDriveLetter":"S:","CacheDriveLetter":"T:","BootPartitionSizeBytes":{{capacity}},"BootAllocationUnitSizeBytes":4096}"""
                : """{"Number":9,"FriendlyName":"Safe USB","SerialNumber":"SERIAL","UniqueId":"UNIQUE","BusType":"USB","IsRemovable":true,"IsSystem":false,"IsBoot":false,"Size":64000000000}""";
            return Task.FromResult(new WinPeProcessExecution { ExitCode = MutationAttempted ? 1 : 0, StandardOutput = output });
        }
        public Task<WinPeProcessExecution> RunCmdScriptAsync(string scriptPath, string scriptArguments, string workingDirectory, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<WinPeProcessExecution> RunCmdScriptDirectAsync(string scriptPath, string scriptArguments, string workingDirectory, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
