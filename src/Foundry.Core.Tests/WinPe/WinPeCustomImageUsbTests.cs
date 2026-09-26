// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text;
using System.Text.Json;
using Foundry.Core.Models.Configuration.Deploy;
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
        Directory.CreateDirectory(root);
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

    private sealed class Publisher(bool fail) : IWinPeCustomImageMediaPublisher
    {
        internal bool PublishAttempted { get; private set; }
        internal IReadOnlyList<string> ValidatedInputs { get; private set; } = [];
        internal long? AdditionalCapacityBytes { get; private set; }
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
            return fail ? Task.FromException(new IOException("Disk full")) : Task.CompletedTask;
        }
    }

    private sealed class Runner(Publisher publisher) : IWinPeProcessRunner
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
                ? """{"BootDriveLetter":"S:","CacheDriveLetter":"T:","BootPartitionSizeBytes":2147483648,"BootAllocationUnitSizeBytes":4096}"""
                : """{"Number":9,"FriendlyName":"Safe USB","SerialNumber":"SERIAL","UniqueId":"UNIQUE","BusType":"USB","IsRemovable":true,"IsSystem":false,"IsBoot":false,"Size":64000000000}""";
            return Task.FromResult(new WinPeProcessExecution { ExitCode = format ? 1 : 0, StandardOutput = output });
        }
        public Task<WinPeProcessExecution> RunCmdScriptAsync(string script, string arguments, string working, CancellationToken token) => throw new NotSupportedException();
        public Task<WinPeProcessExecution> RunCmdScriptDirectAsync(string script, string arguments, string working, CancellationToken token) => throw new NotSupportedException();
    }
}
