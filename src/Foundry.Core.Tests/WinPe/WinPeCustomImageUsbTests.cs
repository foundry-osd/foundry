// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text;
using System.Text.Json;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Core.Models.Packages;
using Foundry.Core.Services.Packages;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeCustomImageUsbTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "foundry-custom-usb-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Update_PublishesDataBeforeFormattingAndStopsOnDataFailure(bool failPublication, bool deployEnabled)
    {
        WinPeTestMediaLayout.Create(root, WinPeArchitecture.X64);
        string bootstrapArchive = Path.Combine(root, "bootstrap.zip");
        string connectArchive = Path.Combine(root, "connect.zip");
        string deployArchive = Path.Combine(root, "deploy.zip");
        await File.WriteAllBytesAsync(bootstrapArchive, new byte[11], TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(connectArchive, new byte[17], TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(deployArchive, new byte[23], TestContext.Current.CancellationToken);
        var publisher = new Publisher(failPublication);
        var runner = new Runner(publisher);
        var service = new WinPeUsbMediaService(runner, new WinPeRuntimePayloadProvisioningService(runner), publisher);
        using var package = new WinPeCustomImageMediaLease("build", Encoding.UTF8.GetBytes("{}"), [], []);
        string config = JsonSerializer.Serialize(new FoundryDeployConfigurationDocument
        {
            CustomImages = new() { IsEnabled = true, ManifestId = package.ManifestId, ManifestHash = package.ManifestHash }
        }, ConfigurationJsonDefaults.SerializerOptions);
        var options = new UsbOutputOptions
        {
            TargetDiskNumber = 9,
            ExpectedDiskFriendlyName = "Safe USB",
            ExpectedDiskSerialNumber = "SERIAL",
            ExpectedDiskUniqueId = "UNIQUE",
            ExpectedDiskBusType = "USB",
            ExpectedDiskSizeBytes = 64000000000,
            CustomImages = package,
            DeployConfigurationJson = config,
            RuntimePayloadProvisioning = new()
            {
                Bootstrap = new() { IsEnabled = true, ArchivePath = bootstrapArchive },
                Connect = new() { IsEnabled = true, ArchivePath = connectArchive },
                Deploy = new() { IsEnabled = deployEnabled, ArchivePath = deployArchive }
            }
        };

        WinPeResult<WinPeUsbProvisionResult> result = await service.UpdateBootPartitionAsync(options,
            new() { WorkingDirectoryPath = root, MediaDirectoryPath = root }, new() { PowerShellPath = "shadowed" }, false,
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.True(publisher.PublishAttempted);
        Assert.Equal(!failPublication, runner.FormattingAttempted);
        Assert.Equal(deployEnabled ? 40L : 17L, publisher.AdditionalCapacityBytes);
        Assert.Contains(root, publisher.ValidatedInputs);
        Assert.Contains(bootstrapArchive, publisher.ValidatedInputs);
        Assert.Contains(connectArchive, publisher.ValidatedInputs);
        if (deployEnabled) Assert.Contains(deployArchive, publisher.ValidatedInputs);
        else Assert.DoesNotContain(deployArchive, publisher.ValidatedInputs);
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task PostInstallationPublicationPrecedesBootMutationAndCombinesImageCapacity(bool withImages, bool fail)
    {
        WinPeTestMediaLayout.Create(root, WinPeArchitecture.X64);
        var publisher = new Publisher(fail);
        var runner = new Runner(publisher);
        var service = new WinPeUsbMediaService(runner, new WinPeRuntimePayloadProvisioningService(runner), publisher, publisher);
        using var images = new WinPeCustomImageMediaLease("images", Encoding.UTF8.GetBytes("{}"), [], []);
        using var post = new WinPeContentPackageMediaLease(ContentPackageKind.PostInstallation, new ContentPackageMediaManifest { Id = "post" }, Encoding.UTF8.GetBytes("{}"), [], [], []);
        string config = JsonSerializer.Serialize(new FoundryDeployConfigurationDocument
        {
            PreOobe = new() { ManifestId = post.ManifestId, ManifestHash = post.ManifestHash },
            CustomImages = new() { IsEnabled = withImages, ManifestId = images.ManifestId, ManifestHash = images.ManifestHash }
        }, ConfigurationJsonDefaults.SerializerOptions);
        var options = new UsbOutputOptions
        {
            TargetDiskNumber = 9,
            ExpectedDiskFriendlyName = "Safe USB",
            ExpectedDiskSerialNumber = "SERIAL",
            ExpectedDiskUniqueId = "UNIQUE",
            ExpectedDiskBusType = "USB",
            ExpectedDiskSizeBytes = 64000000000,
            CustomImages = withImages ? images : null,
            PostInstallation = post,
            DeployConfigurationJson = config
        };
        WinPeResult<WinPeUsbProvisionResult> result = await service.UpdateBootPartitionAsync(options,
            new() { WorkingDirectoryPath = root, MediaDirectoryPath = root }, new() { PowerShellPath = "shadowed" }, false,
            TestContext.Current.CancellationToken);
        Assert.False(result.IsSuccess);
        Assert.True(publisher.PublishAttempted);
        Assert.Equal(!fail, runner.FormattingAttempted);
        Assert.Equal(withImages ? 2048L : 0L, publisher.AdditionalCapacityBytes);
    }

    [Fact]
    public async Task Update_ReportsPublishedContentInsideItsOwnMonotonicRange()
    {
        WinPeTestMediaLayout.Create(root, WinPeArchitecture.X64);
        var publisher = new Publisher(fail: false, reportProgress: true);
        var runner = new Runner(publisher);
        var service = new WinPeUsbMediaService(runner, new WinPeRuntimePayloadProvisioningService(runner), publisher, publisher);
        using var images = new WinPeCustomImageMediaLease("images", Encoding.UTF8.GetBytes("{}"), [], []);
        using var post = new WinPeContentPackageMediaLease(ContentPackageKind.PostInstallation, new ContentPackageMediaManifest { Id = "post" }, Encoding.UTF8.GetBytes("{}"), [], [], []);
        string config = JsonSerializer.Serialize(new FoundryDeployConfigurationDocument
        {
            PreOobe = new() { ManifestId = post.ManifestId, ManifestHash = post.ManifestHash },
            CustomImages = new() { IsEnabled = true, ManifestId = images.ManifestId, ManifestHash = images.ManifestHash }
        }, ConfigurationJsonDefaults.SerializerOptions);
        var progress = new CollectingProgress<WinPeMediaProgress>();
        var options = new UsbOutputOptions
        {
            TargetDiskNumber = 9,
            ExpectedDiskFriendlyName = "Safe USB",
            ExpectedDiskSerialNumber = "SERIAL",
            ExpectedDiskUniqueId = "UNIQUE",
            ExpectedDiskBusType = "USB",
            ExpectedDiskSizeBytes = 64000000000,
            CustomImages = images,
            PostInstallation = post,
            DeployConfigurationJson = config,
            Progress = progress
        };

        _ = await service.UpdateBootPartitionAsync(options,
            new() { WorkingDirectoryPath = root, MediaDirectoryPath = root }, new() { PowerShellPath = "shadowed" }, false,
            TestContext.Current.CancellationToken);

        int[] percents = progress.Reports.Select(report => report.Percent).ToArray();
        Assert.Equal(percents.Order(), percents);
        Assert.All(
            progress.Reports.Where(report => report.Status == Publisher.PublishStatus),
            report => Assert.InRange(report.Percent, 22, 34));
    }

    [Fact]
    public async Task Create_ReportsPublishedContentInsideItsOwnMonotonicRange()
    {
        WinPeTestMediaLayout.Create(root, WinPeArchitecture.X64);
        var publisher = new Publisher(fail: false, reportProgress: true);
        var runner = new CreateRunner();
        var service = new WinPeUsbMediaService(runner, new WinPeRuntimePayloadProvisioningService(runner), publisher, publisher);
        using var images = new WinPeCustomImageMediaLease("images", Encoding.UTF8.GetBytes("{}"), [], []);
        using var post = new WinPeContentPackageMediaLease(ContentPackageKind.PostInstallation, new ContentPackageMediaManifest { Id = "post" }, Encoding.UTF8.GetBytes("{}"), [], [], []);
        string config = JsonSerializer.Serialize(new FoundryDeployConfigurationDocument
        {
            PreOobe = new() { ManifestId = post.ManifestId, ManifestHash = post.ManifestHash },
            CustomImages = new() { IsEnabled = true, ManifestId = images.ManifestId, ManifestHash = images.ManifestHash }
        }, ConfigurationJsonDefaults.SerializerOptions);
        var progress = new CollectingProgress<WinPeMediaProgress>();
        var options = new UsbOutputOptions
        {
            TargetDiskNumber = 9,
            ExpectedDiskFriendlyName = "Safe USB",
            ExpectedDiskSerialNumber = "SERIAL",
            ExpectedDiskUniqueId = "UNIQUE",
            ExpectedDiskBusType = "USB",
            ExpectedDiskSizeBytes = 64000000000,
            PartitionStyle = UsbPartitionStyle.Gpt,
            FormatMode = UsbFormatMode.Quick,
            CustomImages = images,
            PostInstallation = post,
            DeployConfigurationJson = config,
            Progress = progress
        };

        _ = await service.ProvisionAndPopulateAsync(options,
            new() { WorkingDirectoryPath = root, MediaDirectoryPath = root, Architecture = WinPeArchitecture.X64 },
            new() { PowerShellPath = "shadowed" }, false, TestContext.Current.CancellationToken);

        int[] percents = progress.Reports.Select(report => report.Percent).ToArray();
        Assert.Equal(percents.Order(), percents);
        Assert.Contains(progress.Reports, report => report.Status == Publisher.PublishStatus);
        Assert.All(
            progress.Reports.Where(report => report.Status == Publisher.PublishStatus),
            report => Assert.InRange(report.Percent, MediaCreationProgressPlan.UsbFormattingCompletePercent, 80));
    }

    [Fact]
    public async Task UsbPublishesDriverSetsAfterPostInstallation()
    {
        WinPeTestMediaLayout.Create(root, WinPeArchitecture.X64);
        var publisher = new Publisher(fail: false);
        var runner = new Runner(publisher);
        var service = new WinPeUsbMediaService(runner, new WinPeRuntimePayloadProvisioningService(runner), publisher, publisher, publisher);
        using var images = new WinPeCustomImageMediaLease("images", Encoding.UTF8.GetBytes("{}"), [], []);
        using var post = new WinPeContentPackageMediaLease(ContentPackageKind.PostInstallation, new ContentPackageMediaManifest { Id = "post" }, Encoding.UTF8.GetBytes("{}"), [], [], []);
        using var drivers = DriversLease();
        var options = new UsbOutputOptions
        {
            TargetDiskNumber = 9,
            ExpectedDiskFriendlyName = "Safe USB",
            ExpectedDiskSerialNumber = "SERIAL",
            ExpectedDiskUniqueId = "UNIQUE",
            ExpectedDiskBusType = "USB",
            ExpectedDiskSizeBytes = 64000000000,
            CustomImages = images,
            PostInstallation = post,
            CustomDrivers = drivers,
            DeployConfigurationJson = BoundConfiguration(images, post, drivers)
        };

        _ = await service.UpdateBootPartitionAsync(options,
            new() { WorkingDirectoryPath = root, MediaDirectoryPath = root }, new() { PowerShellPath = "shadowed" }, false,
            TestContext.Current.CancellationToken);

        Assert.Equal(["images", ContentPackageKind.PostInstallation.Name, ContentPackageKind.Drivers.Name], publisher.Published);
        Assert.Equal(2048L + 4096L, publisher.AdditionalCapacityBytes);
        Assert.True(runner.FormattingAttempted);
    }

    [Fact]
    public async Task UsbUpdateFailsCapacityBeforeFormattingWhenDriversDoNotFit()
    {
        WinPeTestMediaLayout.Create(root, WinPeArchitecture.X64);
        var publisher = new Publisher(fail: false);
        var runner = new Runner(publisher);
        var driversPublisher = new WinPeContentPackageMediaService(ContentPackageKind.Drivers, _ => 0);
        var service = new WinPeUsbMediaService(runner, new WinPeRuntimePayloadProvisioningService(runner), publisher, publisher, driversPublisher);
        using var drivers = DriversLease();
        var options = new UsbOutputOptions
        {
            TargetDiskNumber = 9,
            ExpectedDiskFriendlyName = "Safe USB",
            ExpectedDiskSerialNumber = "SERIAL",
            ExpectedDiskUniqueId = "UNIQUE",
            ExpectedDiskBusType = "USB",
            ExpectedDiskSizeBytes = 64000000000,
            CustomDrivers = drivers,
            DeployConfigurationJson = BoundConfiguration(null, null, drivers)
        };

        WinPeResult<WinPeUsbProvisionResult> result = await service.UpdateBootPartitionAsync(options,
            new() { WorkingDirectoryPath = root, MediaDirectoryPath = root }, new() { PowerShellPath = "shadowed" }, false,
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(WinPeErrorCodes.CustomImageMediaFailed, result.Error!.Code);
        Assert.Contains("ContentPackage.InsufficientMediaSpace", result.Error.Details);
        Assert.False(publisher.PublishAttempted);
        Assert.False(runner.FormattingAttempted);
    }

    [Fact]
    public async Task UsbUpdateSucceedsWhenPruneFails()
    {
        WinPeTestMediaLayout.Create(root, WinPeArchitecture.X64);
        using TemporaryDriveMapping boot = TemporaryDriveMapping.Create(root, "boot");
        using TemporaryDriveMapping cache = TemporaryDriveMapping.Create(root, "cache");
        WinPeTestMediaLayout.Create(boot.RootPath, WinPeArchitecture.X64);
        var publisher = new Publisher(fail: false, failPrune: true);
        var runner = new Runner(publisher, boot.DriveLetter, cache.DriveLetter, formatSucceeds: true);
        var service = new WinPeUsbMediaService(runner, new WinPeRuntimePayloadProvisioningService(runner), publisher, publisher, publisher);
        using var drivers = DriversLease();
        var progress = new CollectingProgress<WinPeMediaProgress>();
        var options = new UsbOutputOptions
        {
            TargetDiskNumber = 9,
            ExpectedDiskFriendlyName = "Safe USB",
            ExpectedDiskSerialNumber = "SERIAL",
            ExpectedDiskUniqueId = "UNIQUE",
            ExpectedDiskBusType = "USB",
            ExpectedDiskSizeBytes = 64000000000,
            CustomDrivers = drivers,
            DeployConfigurationJson = BoundConfiguration(null, null, drivers),
            Progress = progress
        };

        WinPeResult<WinPeUsbProvisionResult> result = await service.UpdateBootPartitionAsync(options,
            new() { WorkingDirectoryPath = root, MediaDirectoryPath = root }, new() { PowerShellPath = "shadowed" }, false,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.Equal([ContentPackageKind.Drivers.Name], publisher.Pruned);
        Assert.Contains(progress.Reports, report => report.Status == WinPeProgressStatuses.CustomDriversCleanupSkipped);
        Assert.Equal(WinPeProgressStatuses.UsbBootPartitionUpdated, progress.Reports[^1].Status);
    }

    private static WinPeContentPackageMediaLease DriversLease()
    {
        string hash = new('a', 64);
        var manifest = new ContentPackageMediaManifest
        {
            Id = "drivers",
            Packages = [new() { ContentHash = hash, RelativePath = $"Cache/DriverPacks/Custom/Packages/{hash}/files", Manifest = new() }]
        };
        return new WinPeContentPackageMediaLease(ContentPackageKind.Drivers, manifest, Encoding.UTF8.GetBytes("{\"drivers\":1}"), [], [], []);
    }

    private static string BoundConfiguration(WinPeCustomImageMediaLease? images, WinPeContentPackageMediaLease? post, WinPeContentPackageMediaLease drivers) =>
        JsonSerializer.Serialize(new FoundryDeployConfigurationDocument
        {
            PreOobe = post is null ? new() : new() { ManifestId = post.ManifestId, ManifestHash = post.ManifestHash },
            CustomImages = images is null ? new() : new() { IsEnabled = true, ManifestId = images.ManifestId, ManifestHash = images.ManifestHash },
            CustomDrivers = new()
            {
                IsEnabled = true,
                Sets = [new() { Id = "set", DisplayName = "Set", ContentHash = drivers.Manifest.Packages[0].ContentHash, FileCount = 1 }],
                ManifestId = drivers.ManifestId,
                ManifestHash = drivers.ManifestHash
            }
        }, ConfigurationJsonDefaults.SerializerOptions);

    private sealed class Publisher(bool fail, bool reportProgress = false, bool failPrune = false) : IWinPeCustomImageMediaPublisher, IWinPeContentPackageMediaPublisher
    {
        internal const string PublishStatus = "Publishing test content.";
        internal bool PublishAttempted { get; private set; }
        internal List<string> Published { get; } = [];
        internal List<string> Pruned { get; } = [];
        internal IReadOnlyList<string> ValidatedInputs { get; private set; } = [];
        internal long? AdditionalCapacityBytes { get; private set; }
        public Task ValidateSourcesAsync(WinPeContentPackageMediaLease package, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<long> GetRequiredBytesAsync(WinPeContentPackageMediaLease package, string root, CancellationToken cancellationToken) =>
            Task.FromResult(package.Kind == ContentPackageKind.Drivers ? 4096L : 2048L);
        public Task ValidateCapacityAsync(WinPeContentPackageMediaLease package, string root, long extra, CancellationToken cancellationToken)
        {
            AdditionalCapacityBytes = extra;
            return Task.CompletedTask;
        }
        public Task PublishAsync(WinPeContentPackageMediaLease package, string root, CancellationToken token, IProgress<WinPeMediaProgress>? progress = null)
        {
            PublishAttempted = true;
            Published.Add(package.Kind.Name);
            ReportPublishProgress(progress);
            return fail ? Task.FromException(new IOException("Disk full")) : Task.CompletedTask;
        }
        public Task<ContentPackagePruneResult> PruneAsync(WinPeContentPackageMediaLease current, string root, CancellationToken cancellationToken)
        {
            Pruned.Add(current.Kind.Name);
            return failPrune ? Task.FromException<ContentPackagePruneResult>(new IOException("Access denied"))
                : Task.FromResult(new ContentPackagePruneResult(0, 0, []));
        }
        public Task ValidateSourcesAsync(WinPeCustomImageMediaLease package, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ValidateInputDisksAsync(IEnumerable<string> paths, int disk, CancellationToken token)
        {
            ValidatedInputs = paths.ToArray();
            return Task.CompletedTask;
        }
        public Task ValidateCapacityAsync(WinPeCustomImageMediaLease package, string root, long extra, CancellationToken token)
        {
            AdditionalCapacityBytes = extra;
            return Task.CompletedTask;
        }
        public Task ValidateDestinationDiskAsync(string root, int disk, CancellationToken token) => Task.CompletedTask;
        public Task PublishAsync(WinPeCustomImageMediaLease package, string root, CancellationToken token = default, IProgress<WinPeMediaProgress>? progress = null)
        {
            PublishAttempted = true;
            Published.Add("images");
            ReportPublishProgress(progress);
            return fail ? Task.FromException(new IOException("Disk full")) : Task.CompletedTask;
        }

        private void ReportPublishProgress(IProgress<WinPeMediaProgress>? progress)
        {
            if (!reportProgress || progress is null) return;
            foreach (int percent in new[] { 0, 50, 100 })
                progress.Report(new WinPeMediaProgress { Percent = percent, Status = PublishStatus });
        }
    }

    private sealed class Runner(Publisher publisher, string bootDrive = "S:", string cacheDrive = "T:", bool formatSucceeds = false) : IWinPeProcessRunner
    {
        internal bool FormattingAttempted { get; private set; }
        public Task<WinPeProcessExecution> RunAsync(string file, string arguments, string working, CancellationToken token, IReadOnlyDictionary<string, string>? environmentOverrides = null)
        {
            string script = arguments.Contains("-File ", StringComparison.Ordinal)
                ? File.ReadAllText(arguments[(arguments.IndexOf("-File ", StringComparison.Ordinal) + 6)..].Trim('"')) : string.Empty;
            bool layout = script.Contains("$hasFoundryBootPartitionType", StringComparison.Ordinal);
            bool format = script.Contains("Format-Volume", StringComparison.Ordinal);
            if (format)
            {
                Assert.True(publisher.PublishAttempted);
                FormattingAttempted = true;
            }
            string output = layout
                ? $$"""{"BootDriveLetter":"{{bootDrive}}","CacheDriveLetter":"{{cacheDrive}}","BootPartitionSizeBytes":2147483648,"BootAllocationUnitSizeBytes":4096}"""
                : """{"Number":9,"FriendlyName":"Safe USB","SerialNumber":"SERIAL","UniqueId":"UNIQUE","BusType":"USB","IsSystem":false,"IsBoot":false,"Size":64000000000}""";
            return Task.FromResult(new WinPeProcessExecution { ExitCode = format && !formatSucceeds ? 1 : 0, StandardOutput = output });
        }
        public Task<WinPeProcessExecution> RunCmdScriptAsync(string script, string arguments, string working, CancellationToken token) => throw new NotSupportedException();
        public Task<WinPeProcessExecution> RunCmdScriptDirectAsync(string script, string arguments, string working, CancellationToken token) => throw new NotSupportedException();
    }

    /// <summary>Simulates a USB create run: the disk is partitioned first, then the WinPE copy fails.</summary>
    private sealed class CreateRunner : IWinPeProcessRunner
    {
        public Task<WinPeProcessExecution> RunAsync(string file, string arguments, string working, CancellationToken token, IReadOnlyDictionary<string, string>? environmentOverrides = null)
        {
            string script = arguments.Contains("-File ", StringComparison.Ordinal)
                ? File.ReadAllText(arguments[(arguments.IndexOf("-File ", StringComparison.Ordinal) + 6)..].Trim('"')) : string.Empty;
            if (file.EndsWith("robocopy.exe", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(new WinPeProcessExecution { ExitCode = 16 });
            string output = script.Contains("Format-Volume", StringComparison.Ordinal)
                ? "FOUNDRY_USB_PROGRESS|55|USB partitions formatted." + Environment.NewLine +
                  """{"BootPartitionSizeBytes":2147483648,"BootAllocationUnitSizeBytes":4096,"DiskNumber":9,"BootDriveLetter":"S:","CacheDriveLetter":"T:"}"""
                : """{"Number":9,"FriendlyName":"Safe USB","SerialNumber":"SERIAL","UniqueId":"UNIQUE","BusType":"USB","IsSystem":false,"IsBoot":false,"Size":64000000000}""";
            return Task.FromResult(new WinPeProcessExecution { ExitCode = 0, StandardOutput = output });
        }

        public Task<WinPeProcessExecution> RunCmdScriptAsync(string script, string arguments, string working, CancellationToken token) => throw new NotSupportedException();
        public Task<WinPeProcessExecution> RunCmdScriptDirectAsync(string script, string arguments, string working, CancellationToken token) => throw new NotSupportedException();
    }

    private sealed class CollectingProgress<T> : IProgress<T>
    {
        public List<T> Reports { get; } = [];

        public void Report(T value)
        {
            Reports.Add(value);
        }
    }
}
