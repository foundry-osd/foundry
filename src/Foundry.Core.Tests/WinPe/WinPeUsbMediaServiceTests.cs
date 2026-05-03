using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeUsbMediaServiceTests
{
    [Fact]
    public async Task GetUsbCandidatesAsync_FiltersUnsafeDisksAndParsesCandidates()
    {
        using TempWorkspace workspace = TempWorkspace.Create();
        string payload = """
                         [
                           {"Number":3,"FriendlyName":"Safe USB","DriveLetters":"E:","SerialNumber":"USB123","UniqueId":"USB-ID","BusType":"USB","IsRemovable":true,"IsSystem":false,"IsBoot":false,"Size":64000000000},
                           {"Number":4,"FriendlyName":"SATA Disk","DriveLetters":"F:","SerialNumber":"SATA123","UniqueId":"SATA-ID","BusType":"SATA","IsRemovable":true,"IsSystem":false,"IsBoot":false,"Size":64000000000},
                           {"Number":5,"FriendlyName":"Fixed USB","DriveLetters":"G:","SerialNumber":"USB456","UniqueId":"USB-ID-2","BusType":"USB","IsRemovable":false,"IsSystem":false,"IsBoot":false,"Size":64000000000},
                           {"Number":6,"FriendlyName":"System USB","DriveLetters":"H:","SerialNumber":"USB789","UniqueId":"USB-ID-3","BusType":"USB","IsRemovable":true,"IsSystem":true,"IsBoot":false,"Size":64000000000}
                         ]
                         """;
        var service = new WinPeUsbMediaService(new FakeRunner(payload));

        WinPeResult<IReadOnlyList<WinPeUsbDiskCandidate>> result = await service.GetUsbCandidatesAsync(
            new WinPeToolPaths { PowerShellPath = "pwsh.exe" },
            workspace.RootPath,
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
        WinPeUsbDiskCandidate candidate = Assert.Single(result.Value!);
        Assert.Equal(3, candidate.DiskNumber);
        Assert.Equal("Safe USB", candidate.FriendlyName);
        Assert.Equal("E:", candidate.DriveLetters);
        Assert.Equal((ulong)64000000000, candidate.SizeBytes);
    }

    [Fact]
    public void ValidateDiskSafety_WhenTargetIsNotUsb_ReturnsUnsafeTarget()
    {
        WinPeResult result = WinPeUsbMediaService.ValidateDiskSafety(
            new UsbOutputOptions
            {
                TargetDiskNumber = 1,
                ExpectedDiskFriendlyName = "Internal"
            },
            new WinPeUsbDiskIdentity
            {
                Number = 1,
                FriendlyName = "Internal SSD",
                BusType = "NVMe",
                IsRemovable = true
            });

        Assert.False(result.IsSuccess);
        Assert.Equal(WinPeErrorCodes.UsbUnsafeTarget, result.Error?.Code);
    }

    [Fact]
    public void ValidateDiskSafety_WhenIdentityChanged_ReturnsIdentityMismatch()
    {
        WinPeResult result = WinPeUsbMediaService.ValidateDiskSafety(
            new UsbOutputOptions
            {
                TargetDiskNumber = 2,
                ExpectedDiskFriendlyName = "Expected USB",
                ExpectedDiskSerialNumber = "SERIAL-1",
                ExpectedDiskUniqueId = "UNIQUE-1"
            },
            new WinPeUsbDiskIdentity
            {
                Number = 2,
                FriendlyName = "Other USB",
                SerialNumber = "SERIAL-2",
                UniqueId = "UNIQUE-2",
                BusType = "USB",
                IsRemovable = true
            });

        Assert.False(result.IsSuccess);
        Assert.Equal(WinPeErrorCodes.UsbIdentityMismatch, result.Error?.Code);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(7, true)]
    [InlineData(8, false)]
    public void IsRobocopySuccessExitCode_AcceptsDocumentedSuccessRange(int exitCode, bool expected)
    {
        Assert.Equal(expected, WinPeUsbMediaService.IsRobocopySuccessExitCode(exitCode));
    }

    [Fact]
    public void VerifyBootArtifacts_WhenRequiredFilesExist_ReturnsSuccess()
    {
        using TempWorkspace workspace = TempWorkspace.Create();
        Directory.CreateDirectory(Path.Combine(workspace.RootPath, "sources"));
        Directory.CreateDirectory(Path.Combine(workspace.RootPath, "boot"));
        Directory.CreateDirectory(Path.Combine(workspace.RootPath, "EFI", "Boot"));
        File.WriteAllText(Path.Combine(workspace.RootPath, "sources", "boot.wim"), "wim");
        File.WriteAllText(Path.Combine(workspace.RootPath, "boot", "BCD"), "bcd");
        File.WriteAllText(Path.Combine(workspace.RootPath, "EFI", "Boot", "bootx64.efi"), "efi");

        WinPeResult result = WinPeUsbMediaService.VerifyBootArtifacts(workspace.RootPath, WinPeArchitecture.X64);

        Assert.True(result.IsSuccess, result.Error?.Details);
    }

    [Fact]
    public void VerifyBootArtifacts_WhenBootWimIsMissing_ReturnsVerificationFailed()
    {
        using TempWorkspace workspace = TempWorkspace.Create();

        WinPeResult result = WinPeUsbMediaService.VerifyBootArtifacts(workspace.RootPath, WinPeArchitecture.X64);

        Assert.False(result.IsSuccess);
        Assert.Equal(WinPeErrorCodes.UsbVerificationFailed, result.Error?.Code);
    }

    [Fact]
    public void VerifyBootPartitionLayout_WhenFoundryDirectoryExistsOnBootPartition_ReturnsVerificationFailed()
    {
        using TempWorkspace workspace = TempWorkspace.Create();
        Directory.CreateDirectory(Path.Combine(workspace.RootPath, "Foundry"));

        WinPeResult result = WinPeUsbMediaService.VerifyBootPartitionLayout(workspace.RootPath);

        Assert.False(result.IsSuccess);
        Assert.Equal(WinPeErrorCodes.UsbVerificationFailed, result.Error?.Code);
    }

    [Fact]
    public void VerifyBootPartitionLayout_WhenBootPartitionIsMinimal_ReturnsSuccess()
    {
        using TempWorkspace workspace = TempWorkspace.Create();
        Directory.CreateDirectory(Path.Combine(workspace.RootPath, "Boot"));
        Directory.CreateDirectory(Path.Combine(workspace.RootPath, "EFI"));
        Directory.CreateDirectory(Path.Combine(workspace.RootPath, "sources"));
        File.WriteAllText(Path.Combine(workspace.RootPath, "bootmgr"), "boot");
        File.WriteAllText(Path.Combine(workspace.RootPath, "bootmgr.efi"), "efi");

        WinPeResult result = WinPeUsbMediaService.VerifyBootPartitionLayout(workspace.RootPath);

        Assert.True(result.IsSuccess, result.Error?.Details);
    }

    [Fact]
    public void BuildDiskPartScript_WhenGptQuickFormat_CreatesBootAndCachePartitionsWithoutActive()
    {
        IReadOnlyList<string> script = WinPeUsbMediaService.BuildDiskPartScript(
            diskNumber: 7,
            partitionStyle: UsbPartitionStyle.Gpt,
            formatMode: UsbFormatMode.Quick,
            bootDriveLetter: 'S',
            cacheDriveLetter: 'T');

        Assert.Contains("select disk 7", script);
        Assert.Contains("convert mbr noerr", script);
        Assert.Contains("convert gpt", script);
        Assert.Contains("create partition primary size=4096", script);
        Assert.Contains("format fs=fat32 quick label=BOOT", script);
        Assert.Contains("format fs=ntfs quick label=\"Foundry Cache\"", script);
        Assert.DoesNotContain("active", script);
    }

    [Fact]
    public void BuildDiskPartScript_WhenMbrCompleteFormat_MarksBootPartitionActiveWithoutQuickFormat()
    {
        IReadOnlyList<string> script = WinPeUsbMediaService.BuildDiskPartScript(
            diskNumber: 8,
            partitionStyle: UsbPartitionStyle.Mbr,
            formatMode: UsbFormatMode.Complete,
            bootDriveLetter: 'U',
            cacheDriveLetter: 'V');

        Assert.Contains("convert mbr", script);
        Assert.DoesNotContain("convert mbr noerr", script);
        Assert.DoesNotContain("convert gpt", script);
        Assert.Contains("format fs=fat32 label=BOOT", script);
        Assert.Contains("active", script);
        Assert.Contains("format fs=ntfs label=\"Foundry Cache\"", script);
    }

    [Fact]
    public void InitializeCachePartitionDirectories_CreatesUsbCacheLayoutWithoutLogs()
    {
        using TempWorkspace workspace = TempWorkspace.Create();

        WinPeUsbMediaService.InitializeCachePartitionDirectories(workspace.RootPath);

        Assert.True(Directory.Exists(Path.Combine(workspace.RootPath, "Runtime")));
        Assert.True(Directory.Exists(Path.Combine(workspace.RootPath, "Cache", "OperatingSystems")));
        Assert.True(Directory.Exists(Path.Combine(workspace.RootPath, "Cache", "DriverPacks")));
        Assert.True(Directory.Exists(Path.Combine(workspace.RootPath, "Cache", "Firmware")));
        Assert.True(Directory.Exists(Path.Combine(workspace.RootPath, "State")));
        Assert.True(Directory.Exists(Path.Combine(workspace.RootPath, "Temp")));
        Assert.False(Directory.Exists(Path.Combine(workspace.RootPath, "Logs")));
    }

    [Fact]
    public void CreateUsbRuntimePayloadOptions_ScopesRuntimeToCachePartition()
    {
        using TempWorkspace workspace = TempWorkspace.Create();
        var runtimeOptions = new WinPeRuntimePayloadProvisioningOptions
        {
            MountedImagePath = Path.Combine(workspace.RootPath, "mount"),
            UsbCacheRootPath = Path.Combine(workspace.RootPath, "old-cache"),
            WorkingDirectoryPath = Path.Combine(workspace.RootPath, "runtime-work"),
            Connect = new WinPeRuntimePayloadApplicationOptions { IsEnabled = true },
            Deploy = new WinPeRuntimePayloadApplicationOptions { IsEnabled = true }
        };
        var artifact = new WinPeBuildArtifact
        {
            WorkingDirectoryPath = Path.Combine(workspace.RootPath, "work"),
            Architecture = WinPeArchitecture.Arm64
        };
        string cacheRoot = Path.Combine(workspace.RootPath, "cache");

        WinPeRuntimePayloadProvisioningOptions result = WinPeUsbMediaService.CreateUsbRuntimePayloadOptions(
            runtimeOptions,
            artifact,
            cacheRoot);

        Assert.Equal(cacheRoot, result.UsbCacheRootPath);
        Assert.Equal(string.Empty, result.MountedImagePath);
        Assert.Equal(WinPeArchitecture.Arm64, result.Architecture);
        Assert.Same(runtimeOptions.Connect, result.Connect);
        Assert.Same(runtimeOptions.Deploy, result.Deploy);
    }

    [Fact]
    public void ConfigureBootFiles_WhenBootExBinaryExists_ReplacesArchitectureAndMicrosoftBootManagers()
    {
        using TempWorkspace workspace = TempWorkspace.Create();
        string bootRoot = Path.Combine(workspace.RootPath, "boot-root");
        string workingDirectory = Path.Combine(workspace.RootPath, "work");
        Directory.CreateDirectory(Path.Combine(bootRoot, "EFI", "Boot"));
        Directory.CreateDirectory(Path.Combine(workingDirectory, "bootbins"));
        string bootExPath = Path.Combine(workingDirectory, "bootbins", "bootmgfw_EX.efi");
        File.WriteAllText(bootExPath, "bootex");
        File.WriteAllText(Path.Combine(bootRoot, "EFI", "Boot", "bootx64.efi"), "old");

        WinPeResult result = WinPeUsbMediaService.ConfigureBootFiles(
            bootRoot,
            new WinPeBuildArtifact
            {
                WorkingDirectoryPath = workingDirectory,
                Architecture = WinPeArchitecture.X64
            });

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.Equal("bootex", File.ReadAllText(Path.Combine(bootRoot, "EFI", "Boot", "bootx64.efi")));
        Assert.Equal("bootex", File.ReadAllText(Path.Combine(bootRoot, "EFI", "Microsoft", "Boot", "bootmgfw.efi")));
    }

    [Fact]
    public async Task ProvisionAndPopulateAsync_WhenTargetDiskNumberIsMissing_ReturnsValidationFailure()
    {
        var service = new WinPeUsbMediaService(new FakeRunner("{}"));

        WinPeResult<WinPeUsbProvisionResult> result = await service.ProvisionAndPopulateAsync(
            new UsbOutputOptions(),
            new WinPeBuildArtifact(),
            new WinPeToolPaths { PowerShellPath = "pwsh.exe" },
            useBootEx: false,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(WinPeErrorCodes.ValidationFailed, result.Error?.Code);
    }

    [Fact]
    public async Task ProvisionAndPopulateAsync_WhenTargetIdentityIsUnsafe_ReturnsUnsafeTargetBeforeFormatting()
    {
        string payload = """
                         {"Number":9,"FriendlyName":"Internal SSD","SerialNumber":"SERIAL","UniqueId":"UNIQUE","BusType":"NVMe","IsRemovable":true,"IsSystem":false,"IsBoot":false,"Size":64000000000}
                         """;
        var runner = new FakeRunner(payload);
        using TempWorkspace workspace = TempWorkspace.Create();
        var service = new WinPeUsbMediaService(runner);

        WinPeResult<WinPeUsbProvisionResult> result = await service.ProvisionAndPopulateAsync(
            new UsbOutputOptions
            {
                TargetDiskNumber = 9,
                ExpectedDiskFriendlyName = "Internal"
            },
            new WinPeBuildArtifact { WorkingDirectoryPath = workspace.RootPath },
            new WinPeToolPaths { PowerShellPath = "pwsh.exe" },
            useBootEx: false,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(WinPeErrorCodes.UsbUnsafeTarget, result.Error?.Code);
        Assert.Single(runner.Executions);
        Assert.Contains("EncodedCommand", runner.Executions[0].Arguments, StringComparison.Ordinal);
    }

    private sealed class FakeRunner(string output) : IWinPeProcessRunner
    {
        public List<WinPeProcessExecution> Executions { get; } = [];

        public Task<WinPeProcessExecution> RunAsync(
            string fileName,
            string arguments,
            string workingDirectory,
            CancellationToken cancellationToken,
            IReadOnlyDictionary<string, string>? environmentOverrides = null)
        {
            var execution = new WinPeProcessExecution
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                ExitCode = 0,
                StandardOutput = output
            };
            Executions.Add(execution);
            return Task.FromResult(execution);
        }

        public Task<WinPeProcessExecution> RunCmdScriptAsync(
            string scriptPath,
            string scriptArguments,
            string workingDirectory,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<WinPeProcessExecution> RunCmdScriptDirectAsync(
            string scriptPath,
            string scriptArguments,
            string workingDirectory,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class TempWorkspace : IDisposable
    {
        private TempWorkspace(string rootPath)
        {
            RootPath = rootPath;
        }

        public string RootPath { get; }

        public static TempWorkspace Create()
        {
            string rootPath = Path.Combine(Path.GetTempPath(), $"foundry-usb-{Guid.NewGuid():N}");
            Directory.CreateDirectory(rootPath);
            return new TempWorkspace(rootPath);
        }

        public void Dispose()
        {
            Directory.Delete(RootPath, recursive: true);
        }
    }
}
