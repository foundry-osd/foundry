// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net;
using System.Security.Cryptography;
using Foundry.Deploy.Models;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Cache;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.Deployment.Steps;
using Foundry.Deploy.Services.Download;
using Foundry.Deploy.Services.Hardware;
using Foundry.Deploy.Services.System;
using Foundry.Utilities.Processes;
using Foundry.Utilities.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class DeploymentPreflightTests
{
    [Theory]
    [InlineData("wf:telnetclient", true, 6736052234UL, DeploymentStepState.Succeeded)]
    [InlineData("wf:microsoft-windows-subsystem-linux", true, 6736052234UL, DeploymentStepState.Succeeded)]
    [InlineData("wf:netfx3", true, 6736052333UL, DeploymentStepState.Failed)]
    [InlineData("wf:netfx3", true, 6736052334UL, DeploymentStepState.Succeeded)]
    [InlineData("wf:netfx3", false, 6736052234UL, DeploymentStepState.Succeeded)]
    [InlineData("wf:wcf-http-activation", true, 6736052333UL, DeploymentStepState.Failed)]
    [InlineData("wf:wcf-http-activation", true, 6736052334UL, DeploymentStepState.Succeeded)]
    public async Task OptionalFeatureCapacity_ReservesSetupMediaOnlyForApplicableEnabledFeatures(
        string featureId, bool enable, ulong targetBytes, DeploymentStepState expected)
    {
        using var fixture = new PipelineFixture
        {
            TargetBytes = targetBytes,
            OptionalFeatureId = featureId,
            OptionalFeatureEnable = enable
        };

        DeploymentStepResult result = await fixture.RunAsync();

        Assert.Equal(expected, result.State);
        Assert.Equal(expected == DeploymentStepState.Succeeded, fixture.Events.Contains("partition"));
        if (expected == DeploymentStepState.Failed)
        {
            Assert.Equal("insufficient_target_capacity", result.Failure?.Code);
        }
    }

    [Fact]
    public async Task TargetBackedMalformedHash_NeverProbesOrPreparesTarget()
    {
        using var fixture = new PipelineFixture { Mode = DeploymentMode.Iso, Failure = "empty_hash_separators" };
        Assert.Equal(DeploymentStepState.Failed, (await fixture.RunAsync()).State);
        Assert.Empty(fixture.Events);
    }

    [Theory]
    [InlineData(6736052233UL, DeploymentStepState.Failed)]
    [InlineData(6736052234UL, DeploymentStepState.Succeeded)]
    public async Task KnownTargetCapacity_RejectsOneByteShortAndAcceptsExactBoundary(ulong targetBytes, DeploymentStepState expected)
    {
        using var fixture = new PipelineFixture { TargetBytes = targetBytes };
        Assert.Equal(expected, (await fixture.RunAsync()).State);
        Assert.Equal(expected == DeploymentStepState.Succeeded, fixture.Events.Contains("partition"));
    }

    [Theory]
    [InlineData(DeploymentMode.Usb, 6736052333UL, false)]
    [InlineData(DeploymentMode.Iso, 6736052437UL, true)]
    public async Task DeferredDriverPackage_BudgetsTargetCopyAndTargetResidentArchive(DeploymentMode mode, ulong targetBytes, bool partitionsBeforeFullCheck)
    {
        using var fixture = new PipelineFixture { Mode = mode, TargetBytes = targetBytes, DeferredDriver = true };
        Assert.Equal(DeploymentStepState.Failed, (await fixture.RunAsync()).State);
        Assert.Equal(partitionsBeforeFullCheck, fixture.Events.Contains("partition"));
        Assert.DoesNotContain("apply:1", fixture.Events);
    }

    [Theory]
    [InlineData("Lenovo", "https://example.test/drivers.exe?version=1", 6736052334UL, DeploymentStepState.Succeeded)]
    [InlineData("Lenovo", "https://example.test/drivers.exe?version=1", 6736052333UL, DeploymentStepState.Failed)]
    [InlineData("Microsoft", "https://example.test/surface.msi?version=1", 6736052334UL, DeploymentStepState.Succeeded)]
    [InlineData("Microsoft", "https://example.test/surface.msi?version=1", 6736052333UL, DeploymentStepState.Failed)]
    public async Task DeferredDriverWithoutFileName_UsesSourceUrlAndBudgetsTargetCopy(
        string manufacturer, string sourceUrl, ulong targetBytes, DeploymentStepState expected)
    {
        using var fixture = new PipelineFixture
        {
            TargetBytes = targetBytes,
            DriverPackOverride = new DriverPackCatalogItem
            {
                Manufacturer = manufacturer,
                DownloadUrl = sourceUrl,
                SizeBytes = 100
            }
        };

        DeploymentStepResult result = await fixture.RunAsync();

        Assert.Equal(expected, result.State);
        Assert.Equal(expected == DeploymentStepState.Succeeded, fixture.Events.Contains("partition"));
        if (expected == DeploymentStepState.Failed)
        {
            Assert.Equal("insufficient_target_capacity", result.Failure?.Code);
        }
    }

    [Theory]
    [InlineData("dead_url")]
    [InlineData("hash_mismatch")]
    [InlineData("missing_edition")]
    [InlineData("ambiguous_edition")]
    [InlineData("invalid_size")]
    [InlineData("small_target")]
    [InlineData("invalid_hash")]
    [InlineData("empty_hash_separators")]
    [InlineData("negative_size")]
    [InlineData("invalid_url")]
    [InlineData("unsupported_edition")]
    [InlineData("target_mapping")]
    public async Task ExternalFailures_NeverPrepareTarget(string condition)
    {
        using var fixture = new PipelineFixture();
        fixture.Failure = condition;
        DeploymentStepResult result = await fixture.RunAsync();

        Assert.Equal(DeploymentStepState.Failed, result.State);
        Assert.NotNull(result.Failure);
        Assert.NotEqual("preflight_not_ready", result.Failure.Code);
        Assert.DoesNotContain("partition", fixture.Events);
        if (condition is "invalid_hash" or "empty_hash_separators" or "negative_size" or "invalid_url" or "unsupported_edition")
        {
            Assert.Empty(fixture.Events);
        }
    }

    [Fact]
    public async Task ExternalFlow_PreparesAndInspectsOnceBeforePartition_AndCarriesSelectedIndex()
    {
        using var fixture = new PipelineFixture();
        Assert.Equal(DeploymentStepState.Succeeded, (await fixture.RunAsync()).State);
        Assert.Equal(["download", "inspect", "inspect", "partition", "apply:1"], fixture.Events);
        Assert.Contains(fixture.Progress, item => item.StepSubProgressPercent == 100 && !item.StepSubProgressIndeterminate);
    }

    [Theory]
    [InlineData(DeploymentMode.Iso)]
    [InlineData(DeploymentMode.Usb)]
    public async Task TargetBackedFlow_ProbesBeforePartition_AndDownloadsAfter(DeploymentMode mode)
    {
        using var fixture = new PipelineFixture { Mode = mode };
        fixture.Storage.AvailableBytes = 0;
        Assert.Equal(DeploymentStepState.Succeeded, (await fixture.RunAsync()).State);
        Assert.Equal(["probe", "probe", "partition", "download", "inspect", "inspect", "apply:1"], fixture.Events);
        Assert.StartsWith(fixture.WindowsRoot, fixture.Context!.RuntimeState.DownloadedOperatingSystemPath, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("unknown_mapping")]
    [InlineData("ram_cache")]
    [InlineData("unknown_space")]
    [InlineData("not_writable")]
    public async Task UnprovenExternalCache_UsesDeferredValidation(string condition)
    {
        using var fixture = new PipelineFixture { Failure = condition };
        Assert.Equal(DeploymentStepState.Succeeded, (await fixture.RunAsync()).State);
        Assert.Equal("probe", fixture.Events[0]);
        Assert.True(fixture.Context!.Preflight!.UsesTargetStorage);
    }

    [Fact]
    public async Task NearFullCache_AuthenticatesAndReusesOccupiedBytes_WithoutSourceAccess()
    {
        using var fixture = new PipelineFixture { Failure = "dead_url" };
        fixture.CreateCache(fixture.Payload);
        fixture.Storage.AvailableBytes = 0;
        Assert.Equal(DeploymentStepState.Succeeded, (await fixture.RunAsync()).State);
        Assert.DoesNotContain("download", fixture.Events);
        Assert.DoesNotContain("probe", fixture.Events);
        Assert.Equal(2, fixture.Events.Count(item => item == "inspect"));
        Assert.Contains(fixture.Progress, item => item.StepSubProgressLabel?.Contains("Checking cache", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task CorruptCache_RedownloadsAndVerifiesBeforePartition()
    {
        using var fixture = new PipelineFixture();
        fixture.CreateCache(new byte[fixture.Payload.Length]);
        fixture.Storage.AvailableBytes = 0;
        Assert.Equal(DeploymentStepState.Succeeded, (await fixture.RunAsync()).State);
        Assert.Equal("download", fixture.Events[0]);
        Assert.True(fixture.Events.IndexOf("download") < fixture.Events.IndexOf("partition"));
    }

    [Fact]
    public async Task CancellationDuringTransfer_NeverPreparesTarget()
    {
        using var fixture = new PipelineFixture { Failure = "cancel" };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.RunAsync());
        Assert.DoesNotContain("partition", fixture.Events);
    }

    [Fact]
    public async Task TargetBackedActualBytes_AreIncludedBeforeApply()
    {
        using var fixture = new PipelineFixture { Mode = DeploymentMode.Iso, Failure = "actual_size" };
        Assert.Equal(DeploymentStepState.Failed, (await fixture.RunAsync()).State);
        Assert.Contains("partition", fixture.Events);
        Assert.DoesNotContain("apply:1", fixture.Events);
    }

    [Theory]
    [InlineData("unknown_target_space")]
    [InlineData("insufficient_target_space")]
    public async Task TargetBackedApply_RequiresKnownAvailableSpace(string condition)
    {
        using var fixture = new PipelineFixture { Mode = DeploymentMode.Iso, Failure = condition };
        fixture.Storage.TargetAvailableBytes = condition == "unknown_target_space" ? null : 1024;
        Assert.Equal(DeploymentStepState.Failed, (await fixture.RunAsync()).State);
        Assert.Contains("partition", fixture.Events);
        Assert.DoesNotContain("apply:1", fixture.Events);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CatalogHash_AllowsMissingOrHyphenSeparatedValues(bool missingHash)
    {
        using var fixture = new PipelineFixture { Failure = missingHash ? "no_hash" : "separated_hash" };
        Assert.Equal(DeploymentStepState.Succeeded, (await fixture.RunAsync()).State);
        Assert.Contains("partition", fixture.Events);
    }

    [Fact]
    public async Task TargetBackedSourceLostAtErasureBoundary_NeverPreparesTarget()
    {
        using var fixture = new PipelineFixture { Mode = DeploymentMode.Iso };
        DeploymentStepExecutionContext context = fixture.CreateContext();
        Assert.Equal(DeploymentStepState.Succeeded, (await fixture.Preflight.ExecuteAsync(context, TestContext.Current.CancellationToken)).State);
        fixture.Failure = "dead_url";
        Assert.Equal(DeploymentStepState.Failed, (await fixture.Prepare.ExecuteAsync(context, TestContext.Current.CancellationToken)).State);
        Assert.DoesNotContain("partition", fixture.Events);
    }

    [Fact]
    public async Task ExternalMappingChangesToTarget_NeverPreparesTarget()
    {
        using var fixture = new PipelineFixture();
        DeploymentStepExecutionContext context = fixture.CreateContext();
        Assert.Equal(DeploymentStepState.Succeeded, (await fixture.Preflight.ExecuteAsync(context, TestContext.Current.CancellationToken)).State);
        fixture.Failure = "target_mapping";
        Assert.Equal(DeploymentStepState.Failed, (await fixture.Prepare.ExecuteAsync(context, TestContext.Current.CancellationToken)).State);
        Assert.DoesNotContain("partition", fixture.Events);
    }

    [Fact]
    public async Task DryRun_DoesNotTransferInspectOrPartition()
    {
        using var fixture = new PipelineFixture { IsDryRun = true };
        Assert.Equal(DeploymentStepState.Succeeded, (await fixture.RunAsync()).State);
        Assert.Empty(fixture.Events);
    }

    [Fact]
    public async Task ReplacedRuntimeImagePath_CannotAuthorizeErasure()
    {
        using var fixture = new PipelineFixture();
        DeploymentStepExecutionContext context = fixture.CreateContext();
        Assert.Equal(DeploymentStepState.Succeeded, (await fixture.Preflight.ExecuteAsync(context, TestContext.Current.CancellationToken)).State);
        context.RuntimeState.DownloadedOperatingSystemPath = Path.Combine(fixture.Root, "unprepared.esd");
        DeploymentStepResult result = await fixture.Prepare.ExecuteAsync(context, TestContext.Current.CancellationToken);
        Assert.Equal(DeploymentStepState.Failed, result.State);
        Assert.DoesNotContain("partition", fixture.Events);
    }

    private sealed class PipelineFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "Foundry.Deploy.Tests", Guid.NewGuid().ToString("N"));
        public string WindowsRoot => Path.Combine(Root, "Windows");
        public string CacheRoot => Path.Combine(Root, "External");
        public byte[] Payload { get; } = [1, 2, 3, 4];
        public List<string> Events { get; } = [];
        public List<DeploymentStepProgress> Progress { get; } = [];
        public FakeStorage Storage { get; } = new();
        public DeploymentMode Mode { get; init; } = DeploymentMode.Usb;
        public bool IsDryRun { get; init; }
        public ulong? TargetBytes { get; init; }
        public bool DeferredDriver { get; init; }
        public DriverPackCatalogItem? DriverPackOverride { get; init; }
        public string? OptionalFeatureId { get; init; }
        public bool OptionalFeatureEnable { get; init; } = true;
        public string Failure { get; set; } = "";
        public DeploymentStepExecutionContext? Context { get; private set; }
        private readonly HttpClient _client;
        private readonly CancellationTokenSource _cancellation = new();
        public PreflightDeploymentStep Preflight { get; }
        public PrepareTargetDiskLayoutStep Prepare { get; }
        private readonly DownloadOperatingSystemImageStep _download;
        private readonly ApplyOperatingSystemImageStep _apply;

        public PipelineFixture()
        {
            Directory.CreateDirectory(Root);
            _client = new HttpClient(new PipelineHttp(this));
            var artifact = new ArtifactDownloadService(NullLogger<ArtifactDownloadService>.Instance, _client);
            var windows = new PipelineWindows(this);
            var probe = new ImageSourceProbe(_client);
            Preflight = new PreflightDeploymentStep(artifact, windows, Storage, probe);
            Prepare = new PrepareTargetDiskLayoutStep(windows, probe);
            _download = new DownloadOperatingSystemImageStep(artifact);
            _apply = new ApplyOperatingSystemImageStep(windows, Storage);
        }

        public void CreateCache(byte[] bytes)
        {
            string path = Path.Combine(CacheRoot, "Cache", "OperatingSystems", "install.esd");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }

        public DeploymentStepExecutionContext CreateContext()
        {
            if (Failure == "unknown_space") Storage.AvailableBytes = null;
            if (Failure == "not_writable") Storage.Writable = false;
            var request = new DeploymentContext
            {
                Mode = Mode,
                CacheRootPath = CacheRoot,
                TargetDiskNumber = 1,
                TargetDiskIdentity = Identity,
                TargetComputerName = "LAB01",
                IsDryRun = IsDryRun,
                DriverPackSelectionKind = DeferredDriver || DriverPackOverride is not null ? DriverPackSelectionKind.OemCatalog : DriverPackSelectionKind.None,
                DriverPack = DriverPackOverride ?? (DeferredDriver ? new DriverPackCatalogItem { Manufacturer = "Lenovo", FileName = "drivers.exe", SizeBytes = 100 } : null),
                WindowsOptionalFeatures = new DeployWindowsOptionalFeatureSettings
                {
                    IsEnabled = OptionalFeatureId is not null,
                    Actions = OptionalFeatureId is null ? [] : [new() { Id = OptionalFeatureId, Enable = OptionalFeatureEnable }]
                },
                OperatingSystem = new OperatingSystemCatalogItem
                {
                    Edition = Failure == "unsupported_edition" ? "Unknown" : "Pro",
                    FileName = "install.esd",
                    Url = Failure == "invalid_url" ? "file:///image" : "https://example.test/image",
                    SizeBytes = Failure == "negative_size" ? -1 : Failure == "actual_size" ? 1 : Payload.Length,
                    Sha256 = Failure switch
                    {
                        "invalid_hash" => new string('G', 64),
                        "empty_hash_separators" => "----",
                        "no_hash" => "",
                        "separated_hash" => BitConverter.ToString(SHA256.HashData(Payload)),
                        _ => Convert.ToHexString(SHA256.HashData(Payload))
                    }
                }
            };
            Context = new DeploymentStepExecutionContext(request,
                new DeploymentRuntimeState
                {
                    WorkspaceRoot = Path.Combine(Root, "Workspace"),
                    Mode = Mode,
                    ResolvedCache = new CacheResolution { RootPath = Failure == "ram_cache" ? @"X:\Cache" : CacheRoot, Source = "test" }
                }, [], new DriverApplicationOperationProgressService(), new DriverApplicationLogService(), new PipelineDisks(this), Progress.Add);
            return Context;
        }

        public DiskIdentity Identity => new(1, "", "SERIAL-1", "Disk", "SATA", TargetBytes ?? (Failure == "small_target" ? 1024UL :
            Failure == "actual_size" ? 6736052237UL : 64UL * 1024 * 1024 * 1024));

        public async Task<DeploymentStepResult> RunAsync()
        {
            DeploymentStepExecutionContext context = CreateContext();
            IDeploymentStep[] steps = [Preflight, Prepare, _download, _apply];
            DeploymentStepResult result = DeploymentStepResult.Succeeded("start");
            foreach (IDeploymentStep step in steps)
            {
                context.SetCurrentStep(step, 1);
                result = await step.ExecuteAsync(context, _cancellation.Token);
                if (result.State == DeploymentStepState.Failed) break;
            }
            return result;
        }

        public void Dispose()
        {
            Context?.Dispose();
            _client.Dispose();
            _cancellation.Dispose();
            Directory.Delete(Root, true);
        }

        private sealed class PipelineHttp(PipelineFixture fixture) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                fixture.Events.Add(request.Headers.Range is null ? "download" : "probe");
                if (fixture.Failure == "cancel")
                {
                    fixture._cancellation.Cancel();
                    cancellationToken.ThrowIfCancellationRequested();
                }
                return Task.FromResult(new HttpResponseMessage(fixture.Failure == "dead_url" ? HttpStatusCode.NotFound : HttpStatusCode.OK)
                { Content = new ByteArrayContent(fixture.Failure == "hash_mismatch" ? [5, 6, 7, 8] : fixture.Payload) });
            }
        }

        private sealed class PipelineDisks(PipelineFixture fixture) : ITargetDiskService
        {
            public Task<IReadOnlyList<TargetDiskInfo>> GetDisksAsync(CancellationToken cancellationToken = default, bool includeExcludedDisks = false) =>
                Task.FromResult<IReadOnlyList<TargetDiskInfo>>([new() { DiskNumber = 1, Identity = fixture.Identity, SizeBytes = fixture.Identity.SizeBytes, IsSelectable = true, BusType = "SATA" }]);
            public Task<int?> GetDiskNumberForPathAsync(string path, CancellationToken cancellationToken = default) =>
                Task.FromResult<int?>(fixture.Failure == "unknown_mapping" ? null : fixture.Failure == "target_mapping" ? 1 : 2);
        }

        private sealed class PipelineWindows : RecordingDriverApplicationService
        {
            private readonly PipelineFixture _fixture;
            private readonly WindowsDeploymentService _real;
            public PipelineWindows(PipelineFixture fixture)
            {
                _fixture = fixture;
                _real = new WindowsDeploymentService(new PipelineProcess(fixture), NullLogger<WindowsDeploymentService>.Instance);
            }
            public override Task<WindowsImageMetadata> InspectImageAsync(string imagePath, string requestedEdition, string workingDirectory, CancellationToken cancellationToken = default)
                => _real.InspectImageAsync(imagePath, requestedEdition, workingDirectory, cancellationToken);
            public override Task<DeploymentTargetLayout> PrepareTargetDiskAsync(DiskIdentity confirmedIdentity, string workingDirectory, CancellationToken cancellationToken = default)
            {
                _fixture.Events.Add("partition");
                return Task.FromResult(new DeploymentTargetLayout { DiskNumber = 1, SystemPartitionRoot = Path.Combine(_fixture.Root, "System"), WindowsPartitionRoot = _fixture.WindowsRoot, RecoveryPartitionRoot = Path.Combine(_fixture.Root, "Recovery"), RecoveryPartitionLetter = 'R' });
            }
            public override Task ApplyImageAsync(string imagePath, int imageIndex, string windowsPartitionRoot, string scratchDirectory, string workingDirectory, CancellationToken cancellationToken = default, IProgress<double>? progress = null)
            { _fixture.Events.Add($"apply:{imageIndex}"); return Task.CompletedTask; }
            public override Task ConfigureBootAsync(string windowsPartitionRoot, string systemPartitionRoot, int operatingSystemBuildMajor, string workingDirectory, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public override Task<string?> GetAppliedWindowsEditionAsync(string windowsPartitionRoot, string workingDirectory, CancellationToken cancellationToken = default) => Task.FromResult<string?>("Professional");
        }

        private sealed class PipelineProcess(PipelineFixture fixture) : IProcessRunner
        {
            public Task<ProcessExecutionResult> RunAsync(string fileName, string arguments, string workingDirectory, CancellationToken cancellationToken = default)
            {
                Assert.Equal("dism.exe", fileName);
                Assert.Contains("/Get-ImageInfo", arguments, StringComparison.Ordinal);
                fixture.Events.Add("inspect");
                string output = fixture.OptionalFeatureId is not null && arguments.Contains("/Index:2", StringComparison.Ordinal)
                    ? "Index : 2\nName : Windows Setup Media\nSize : 100 bytes\n"
                    : arguments.Contains("/Index:", StringComparison.Ordinal)
                    ? $"Index : 1\nName : Windows 11 Pro\nEdition : {(fixture.Failure == "missing_edition" ? "Core" : "Professional")}\nSize : {(fixture.Failure == "invalid_size" ? "unknown" : "10 bytes")}\n"
                    : "Index : 1\nName : Windows 11 Pro\n" + (fixture.Failure == "ambiguous_edition" ? "Index : 2\nName : Duplicate\n"
                        : fixture.OptionalFeatureId is not null ? "Index : 2\nName : Windows Setup Media\n" : "");
                return Task.FromResult(new ProcessExecutionResult { ExitCode = 0, StandardOutput = output });
            }
            public Task<ProcessExecutionResult> RunAsync(string fileName, IEnumerable<string> arguments, string workingDirectory, CancellationToken cancellationToken = default) => RunAsync(fileName, string.Join(' ', arguments), workingDirectory, cancellationToken);
            public Task<ProcessExecutionResult> RunAsync(string fileName, IEnumerable<string> arguments, string workingDirectory, Action<string>? onOutputData, Action<string>? onErrorData, CancellationToken cancellationToken = default) => RunAsync(fileName, arguments, workingDirectory, cancellationToken);
        }
    }

    private sealed class FakeStorage : IDeploymentStorageService
    {
        public long? AvailableBytes { get; set; } = 64L * 1024 * 1024 * 1024;
        public bool Writable { get; set; } = true;
        public long? TargetAvailableBytes { get; set; } = 64L * 1024 * 1024 * 1024;
        public long? GetAvailableBytes(string path) => path.Contains("Windows", StringComparison.Ordinal) ? TargetAvailableBytes : AvailableBytes;
        public bool CanWriteDirectory(string path) => Writable;
    }
}
