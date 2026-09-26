// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Foundry.Deploy.Models;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Cache;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.Deployment.Steps;
using Foundry.Deploy.Services.Download;
using Foundry.Deploy.Services.Hardware;
using Foundry.Deploy.Services.System;
using Foundry.Telemetry;
using Foundry.Utilities.Processes;
using Foundry.Utilities.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class DeploymentPreflightTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CustomImage_PreflightValidatesExactIndexBeforePartitionWithoutNetwork(bool invalidHash)
    {
        using var fixture = new PipelineFixture();
        fixture.CreateCache(fixture.Payload);
        var index = new Foundry.Core.Models.Configuration.CustomImageIndex { Index = 7, EditionId = "Anything", Architecture = "x86", ExpandedSizeBytes = 4096 };
        fixture.CustomImage = new CustomImageSelection(new CustomImageAsset
        {
            Id = "custom",
            DisplayName = "Custom",
            ImagePath = Path.Combine(fixture.CacheRoot, "Cache", "OperatingSystems", "install.esd"),
            VolumeRoot = fixture.CacheRoot,
            ExpectedLength = fixture.Payload.Length,
            ExpectedHash = invalidHash ? new string('0', 64) : Convert.ToHexString(SHA256.HashData(fixture.Payload))
        }, index);
        var preflight = new PreflightDeploymentStep(fixture.Storage, new ImageSourceProbe(), new CustomReader(index));
        DeploymentStepExecutionContext context = fixture.CreateContext();
        DeploymentStepResult result = await preflight.ExecuteAsync(context, TestContext.Current.CancellationToken);
        Assert.Equal(invalidHash ? DeploymentStepState.Failed : DeploymentStepState.Succeeded, result.State);
        Assert.Empty(fixture.Events);
        if (invalidHash) Assert.Null(context.Preflight);
        else
        {
            Assert.Equal(7, context.Preflight!.Image!.Index);
            Assert.False(context.Preflight.UsesTargetStorage);
            Assert.Throws<IOException>(() => File.Delete(fixture.CustomImage.Asset.ImagePath));
            Assert.Equal(DeploymentStepState.Succeeded, (await fixture.Prepare.ExecuteAsync(context, TestContext.Current.CancellationToken)).State);
            Assert.Equal(["partition"], fixture.Events);
        }
    }

    [Theory]
    [InlineData("target_mapping", 4096, 7, 0)]
    [InlineData("unknown_mapping", 4096, 7, 0)]
    [InlineData("", 0, 7, 0)]
    [InlineData("", 4096, 8, 0)]
    [InlineData("", 4096, 7, 1)]
    public async Task CustomImage_InvalidSourceOrCapacityNeverPartitions(string failure, long expandedSize, int actualIndex, ulong targetBytes)
    {
        using var fixture = new PipelineFixture { Failure = failure, TargetBytes = targetBytes == 0 ? null : targetBytes };
        fixture.CreateCache(fixture.Payload);
        var index = new Foundry.Core.Models.Configuration.CustomImageIndex { Index = 7, ExpandedSizeBytes = expandedSize };
        fixture.CustomImage = new CustomImageSelection(new CustomImageAsset
        {
            Id = "manual",
            DisplayName = "Custom",
            ImagePath = Path.Combine(fixture.CacheRoot, "Cache", "OperatingSystems", "install.esd"),
            VolumeRoot = fixture.CacheRoot
        }, index);
        var preflight = new PreflightDeploymentStep(fixture.Storage, new ImageSourceProbe(), new CustomReader(index with { Index = actualIndex }));
        DeploymentStepExecutionContext context = fixture.CreateContext();
        Assert.Equal(DeploymentStepState.Failed, (await preflight.ExecuteAsync(context, TestContext.Current.CancellationToken)).State);
        Assert.Null(context.Preflight);
        Assert.Empty(fixture.Events);
        File.Delete(fixture.CustomImage.Asset.ImagePath);
    }

    private sealed class CustomReader(Foundry.Core.Models.Configuration.CustomImageIndex index) : Foundry.Core.Services.Images.ICustomImageMetadataReader
    {
        public Task<IReadOnlyList<Foundry.Core.Models.Configuration.CustomImageIndex>> ReadAsync(string path, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Foundry.Core.Models.Configuration.CustomImageIndex>>([index]);
    }
    [Theory]
    [InlineData("iso_cold")]
    [InlineData("usb_cold")]
    [InlineData("usb_warm")]
    [InlineData("usb_corrupt")]
    [InlineData("usb_near_full")]
    [InlineData("usb_read_only")]
    [InlineData("usb_unknown_size")]
    [InlineData("usb_invalid_edition")]
    public async Task OrchestratedImageFlow_UsesResolvedRouteAndKeepsValidationBeforeExternalErasure(string scenario)
    {
        using var fixture = new PipelineFixture
        {
            Mode = scenario == "iso_cold" ? DeploymentMode.Iso : DeploymentMode.Usb,
            Failure = scenario switch
            {
                "usb_read_only" => "not_writable",
                "usb_unknown_size" => "unknown_size",
                "usb_invalid_edition" => "missing_edition",
                "usb_warm" or "usb_near_full" => "dead_url",
                _ => ""
            }
        };
        bool cached = scenario is "usb_warm" or "usb_near_full";
        if (cached) fixture.CreateCache(fixture.Payload);
        if (scenario == "usb_corrupt") fixture.CreateCache(new byte[fixture.Payload.Length]);
        if (scenario == "usb_near_full") fixture.Storage.AvailableBytes = 0;

        DeploymentResult result = await fixture.RunOrchestratedAsync();

        if (scenario == "usb_invalid_edition")
        {
            Assert.False(result.IsSuccess);
            Assert.Equal(["download", "inspect"], fixture.Events);
            Assert.Equal(DeploymentStepNames.CheckWindowsImage, fixture.Context!.RuntimeState.LastFailureStep);
            Assert.DoesNotContain(fixture.Progress, update => update.StepName == DeploymentStepNames.PrepareTargetDiskLayout);
        }
        else
        {
            Assert.True(result.IsSuccess, result.Message);
            bool targetStorage = scenario is "iso_cold" or "usb_read_only" or "usb_unknown_size";
            string[] expectedEvents = targetStorage
                ? ["probe", "probe", "partition", "download", "inspect", "apply:1", "boot"]
                : cached ? ["inspect", "partition", "apply:1", "boot"]
                : ["download", "inspect", "partition", "apply:1", "boot"];
            Assert.Equal(expectedEvents, fixture.Events);
            string imagePath = fixture.Context!.RuntimeState.DownloadedOperatingSystemPath!;
            Assert.StartsWith(targetStorage ? fixture.WindowsRoot : fixture.CacheRoot, imagePath, StringComparison.OrdinalIgnoreCase);
            DeploymentStepOutcome download = Assert.Single(fixture.Context.RuntimeState.StepOutcomes,
                outcome => outcome.Name == DeploymentStepNames.DownloadOperatingSystemImage);
            Assert.Equal(cached ? DeploymentStepState.Skipped : DeploymentStepState.Succeeded, download.State);
            Assert.Single(fixture.Context.RuntimeState.StepOutcomes, outcome => outcome.Name == DeploymentStepNames.ConfigureWindowsBoot);
        }

        Assert.Null(fixture.Context!.Preflight);
        // Terminal orchestration must release the lease even when image validation failed.
        using var exclusiveImage = new FileStream(fixture.Context.RuntimeState.DownloadedOperatingSystemPath!,
            FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Fact]
    public async Task OrchestratedExternalImage_WhenCancelledAfterInspection_ReleasesLeaseWithoutErasingTarget()
    {
        using var fixture = new PipelineFixture { Failure = "cancel_after_check" };

        DeploymentResult result = await fixture.RunOrchestratedAsync();

        Assert.True(result.IsCancelled);
        Assert.Equal(["download", "inspect"], fixture.Events);
        Assert.Null(fixture.Context!.Preflight);
        using var exclusiveImage = new FileStream(fixture.Context.RuntimeState.DownloadedOperatingSystemPath!,
            FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Fact]
    public async Task OrchestratedExternalImage_RepeatedDeploymentRevalidatesCachedImageAndCreatesFreshReadiness()
    {
        using var fixture = new PipelineFixture();
        Assert.True((await fixture.RunOrchestratedAsync()).IsSuccess);
        DeploymentStepExecutionContext previousContext = fixture.Context!;
        fixture.Events.Clear();
        fixture.Progress.Clear();
        fixture.Failure = "dead_url";

        DeploymentResult result = await fixture.RunOrchestratedAsync();

        Assert.True(result.IsSuccess, result.Message);
        Assert.NotSame(previousContext, fixture.Context);
        Assert.Equal(["inspect", "partition", "apply:1", "boot"], fixture.Events);
        Assert.Single(fixture.Context!.RuntimeState.StepOutcomes,
            outcome => outcome.Name == DeploymentStepNames.PrepareTargetDiskLayout);
        Assert.Null(previousContext.Preflight);
        Assert.Null(fixture.Context.Preflight);
    }

    [Fact]
    public async Task ExternalPreflight_OnlyPlansStorage_WithoutDownloadingOrInspectingImage()
    {
        using var fixture = new PipelineFixture();
        DeploymentStepExecutionContext context = fixture.CreateContext();

        DeploymentStepResult result = await fixture.Preflight.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(DeploymentStepState.Succeeded, result.State);
        Assert.False(context.Preflight!.UsesTargetStorage);
        Assert.Null(context.RuntimeState.DownloadedOperatingSystemPath);
        Assert.Empty(fixture.Events);
        Assert.Equal(DeploymentStepState.Failed,
            (await fixture.Prepare.ExecuteAsync(context, TestContext.Current.CancellationToken)).State);
    }

    [Theory]
    [InlineData(false, DeploymentStepState.Succeeded)]
    [InlineData(true, DeploymentStepState.Skipped)]
    public async Task ExternalDownload_PreservesAcquiredImage_AndRequiresImageCheckBeforeErasure(bool cached, DeploymentStepState expected)
    {
        using var fixture = new PipelineFixture();
        if (cached) fixture.CreateCache(fixture.Payload);
        DeploymentStepExecutionContext context = fixture.CreateContext();
        await fixture.Preflight.ExecuteAsync(context, TestContext.Current.CancellationToken);

        DeploymentStepResult download = await fixture.Download.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(expected, download.State);
        Assert.NotNull(context.RuntimeState.DownloadedOperatingSystemPath);
        Assert.Equal(fixture.Payload, await File.ReadAllBytesAsync(context.RuntimeState.DownloadedOperatingSystemPath!, TestContext.Current.CancellationToken));
        Assert.DoesNotContain("inspect", fixture.Events);
        Assert.Equal(DeploymentStepState.Failed, (await fixture.Prepare.ExecuteAsync(context, TestContext.Current.CancellationToken)).State);
        Assert.Throws<IOException>(() => File.WriteAllBytes(context.RuntimeState.DownloadedOperatingSystemPath!, [0]));
        Assert.Equal(DeploymentStepState.Succeeded, (await fixture.CheckImage.ExecuteAsync(context, TestContext.Current.CancellationToken)).State);
        Assert.Equal(DeploymentStepState.Succeeded, (await fixture.Prepare.ExecuteAsync(context, TestContext.Current.CancellationToken)).State);
    }

    [Fact]
    public async Task ApplyImage_DoesNotConfigureBoot_UntilDedicatedBootStepRuns()
    {
        using var fixture = new PipelineFixture();

        Assert.Equal(DeploymentStepState.Succeeded, (await fixture.RunAsync()).State);

        Assert.Contains("apply:1", fixture.Events);
        Assert.DoesNotContain("boot", fixture.Events);
        Assert.Equal(DeploymentStepState.Succeeded, (await fixture.Boot.ExecuteAsync(fixture.Context!, TestContext.Current.CancellationToken)).State);
        Assert.Equal("boot", fixture.Events[^1]);
    }

    [Fact]
    public async Task ConfigureBoot_WithoutAppliedImage_DoesNotCreateBootFiles()
    {
        using var fixture = new PipelineFixture();

        DeploymentStepResult result = await fixture.Boot.ExecuteAsync(fixture.CreateContext(), TestContext.Current.CancellationToken);

        Assert.Equal(DeploymentStepState.Failed, result.State);
        Assert.Equal("missing_applied_image", result.Failure?.Code);
        Assert.DoesNotContain("boot", fixture.Events);
    }

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
    [InlineData("native_image_error")]
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
        Assert.Equal(["download", "inspect", "partition", "apply:1"], fixture.Events);
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
        Assert.Equal(["probe", "probe", "partition", "download", "inspect", "apply:1"], fixture.Events);
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
        Assert.Single(fixture.Events, item => item == "inspect");
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
        Assert.Equal(DeploymentStepState.Succeeded, (await fixture.Download.ExecuteAsync(context, TestContext.Current.CancellationToken)).State);
        Assert.Equal(DeploymentStepState.Succeeded, (await fixture.CheckImage.ExecuteAsync(context, TestContext.Current.CancellationToken)).State);
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
        Assert.Equal(DeploymentStepState.Succeeded, (await fixture.Download.ExecuteAsync(context, TestContext.Current.CancellationToken)).State);
        Assert.Equal(DeploymentStepState.Succeeded, (await fixture.CheckImage.ExecuteAsync(context, TestContext.Current.CancellationToken)).State);
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
        public CustomImageSelection? CustomImage { get; set; }
        public DeploymentStepExecutionContext? Context { get; private set; }
        private readonly HttpClient _client;
        private readonly CancellationTokenSource _cancellation = new();
        public PreflightDeploymentStep Preflight { get; }
        public PrepareTargetDiskLayoutStep Prepare { get; }
        public DownloadOperatingSystemImageStep Download { get; }
        public CheckWindowsImageStep CheckImage { get; }
        public ConfigureWindowsBootStep Boot { get; }
        private readonly ApplyOperatingSystemImageStep _apply;

        public PipelineFixture()
        {
            Directory.CreateDirectory(Root);
            _client = new HttpClient(new PipelineHttp(this));
            var artifact = new ArtifactDownloadService(NullLogger<ArtifactDownloadService>.Instance, _client);
            var windows = new PipelineWindows(this);
            var probe = new ImageSourceProbe(_client);
            Preflight = new PreflightDeploymentStep(Storage, probe);
            Prepare = new PrepareTargetDiskLayoutStep(windows, probe);
            Download = new DownloadOperatingSystemImageStep(artifact);
            CheckImage = new CheckWindowsImageStep(windows, Storage);
            Boot = new ConfigureWindowsBootStep(windows);
            _apply = new ApplyOperatingSystemImageStep(windows, Storage);
        }

        public void CreateCache(byte[] bytes)
        {
            string path = Path.Combine(CacheRoot, "Cache", "OperatingSystems", "install.esd");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }

        private DeploymentContext CreateRequest()
        {
            if (Failure == "unknown_space") Storage.AvailableBytes = null;
            if (Failure == "not_writable") Storage.Writable = false;
            return new DeploymentContext
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
                OperatingSystem = (OperatingSystemMetadata?)CustomImage ?? new OperatingSystemCatalogItem
                {
                    Edition = Failure == "unsupported_edition" ? "Unknown" : "Pro",
                    FileName = "install.esd",
                    Url = Failure == "invalid_url" ? "file:///image" : "https://example.test/image",
                    SizeBytes = Failure == "negative_size" ? -1 : Failure == "unknown_size" ? 0 : Failure == "actual_size" ? 1 : Payload.Length,
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
        }

        public DeploymentStepExecutionContext CreateContext()
        {
            Context = new DeploymentStepExecutionContext(CreateRequest(),
                new DeploymentRuntimeState
                {
                    WorkspaceRoot = Path.Combine(Root, "Workspace"),
                    Mode = Mode,
                    ResolvedCache = new CacheResolution { RootPath = Failure == "ram_cache" ? @"X:\Cache" : CacheRoot, Source = "test" }
                }, [], new DriverApplicationOperationProgressService(), new DriverApplicationLogService(), new PipelineDisks(this), Progress.Add);
            return Context;
        }

        public async Task<DeploymentResult> RunOrchestratedAsync()
        {
            var disks = new PipelineDisks(this);
            IDeploymentStep[] steps = DeploymentStepNames.ExecutionOrder.Select(name => (IDeploymentStep)(name switch
            {
                DeploymentStepNames.ValidateTargetConfiguration => new ValidateTargetConfigurationStep(new PipelineHardware()),
                DeploymentStepNames.ResolveCacheStrategy => new ResolveCacheStrategyStep(
                    new CacheLocatorService(NullLogger<CacheLocatorService>.Instance), disks),
                DeploymentStepNames.PreflightDeployment => new CapturingPreflightStep(this),
                DeploymentStepNames.PrepareTargetDiskLayout => Prepare,
                DeploymentStepNames.DownloadOperatingSystemImage => Download,
                DeploymentStepNames.CheckWindowsImage => CheckImage,
                DeploymentStepNames.ApplyOperatingSystemImage => _apply,
                DeploymentStepNames.ConfigureWindowsBoot => Boot,
                _ => new UnrelatedStep(name)
            })).ToArray();
            var orchestrator = new DeploymentOrchestrator(new DriverApplicationOperationProgressService(),
                new DriverApplicationLogService(), disks, steps, new NullTelemetryService(),
                NullLogger<DeploymentOrchestrator>.Instance);
            orchestrator.StepProgressChanged += (_, update) =>
            {
                Progress.Add(update);
                if (Failure == "cancel_after_check" && update.StepName == DeploymentStepNames.CheckWindowsImage &&
                    update.State == DeploymentStepState.Succeeded)
                {
                    _cancellation.Cancel();
                }
            };
            return await orchestrator.RunAsync(CreateRequest() with { ApplyFirmwareUpdates = false }, _cancellation.Token);
        }

        private sealed class CapturingPreflightStep(PipelineFixture fixture) : IDeploymentStep
        {
            public string Name => DeploymentStepNames.PreflightDeployment;
            public Task<DeploymentStepResult> ExecuteAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
            {
                fixture.Context = context;
                return fixture.Preflight.ExecuteAsync(context, cancellationToken);
            }
        }

        private sealed class UnrelatedStep(string name) : IDeploymentStep
        {
            public string Name => name;
            public Task<DeploymentStepResult> ExecuteAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
                => Task.FromResult(DeploymentStepResult.Succeeded("Unrelated stage completed."));
        }

        private sealed class PipelineHardware : IHardwareProfileService
        {
            public Task<HardwareProfile> GetCurrentAsync(CancellationToken cancellationToken = default)
                => Task.FromResult(new HardwareProfile());
        }

        public DiskIdentity Identity => new(1, "", "SERIAL-1", "Disk", "SATA", TargetBytes ?? (Failure == "small_target" ? 1024UL :
            Failure == "actual_size" ? 6736052237UL : 64UL * 1024 * 1024 * 1024));

        public async Task<DeploymentStepResult> RunAsync()
        {
            DeploymentStepExecutionContext context = CreateContext();
            DeploymentStepResult result = await Preflight.ExecuteAsync(context, _cancellation.Token);
            if (result.State == DeploymentStepState.Failed) return result;
            IDeploymentStep[] steps = context.Preflight!.UsesTargetStorage
                ? [Prepare, Download, CheckImage, _apply]
                : [Download, CheckImage, Prepare, _apply];
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
                _real = new WindowsDeploymentService(new RejectingProcessRunner(), NullLogger<WindowsDeploymentService>.Instance, new PipelineImageInfoReader(fixture));
            }
            public override Task<WindowsImageMetadata> InspectImageAsync(string imagePath, string requestedEdition, CancellationToken cancellationToken = default)
                => _real.InspectImageAsync(imagePath, requestedEdition, cancellationToken);
            public override Task<DeploymentTargetLayout> PrepareTargetDiskAsync(DiskIdentity confirmedIdentity, string workingDirectory, CancellationToken cancellationToken = default)
            {
                _fixture.Events.Add("partition");
                return Task.FromResult(new DeploymentTargetLayout { DiskNumber = 1, SystemPartitionRoot = Path.Combine(_fixture.Root, "System"), WindowsPartitionRoot = _fixture.WindowsRoot, RecoveryPartitionRoot = Path.Combine(_fixture.Root, "Recovery"), RecoveryPartitionLetter = 'R' });
            }
            public override Task ApplyImageAsync(string imagePath, int imageIndex, string windowsPartitionRoot, string scratchDirectory, string workingDirectory, CancellationToken cancellationToken = default, IProgress<double>? progress = null)
            { _fixture.Events.Add($"apply:{imageIndex}"); return Task.CompletedTask; }
            public override Task ConfigureBootAsync(string windowsPartitionRoot, string systemPartitionRoot, int operatingSystemBuildMajor, string workingDirectory, CancellationToken cancellationToken = default)
            {
                Assert.Equal(_fixture.WindowsRoot, windowsPartitionRoot);
                Assert.Equal(Path.Combine(_fixture.Root, "System"), systemPartitionRoot);
                _fixture.Events.Add("boot");
                return Task.CompletedTask;
            }
            public override Task<string?> GetAppliedWindowsEditionAsync(string windowsPartitionRoot, string workingDirectory, CancellationToken cancellationToken = default) => Task.FromResult<string?>("Professional");
        }

        private sealed class RejectingProcessRunner : IProcessRunner
        {
            public Task<ProcessExecutionResult> RunAsync(string fileName, string arguments, string workingDirectory, CancellationToken cancellationToken = default)
                => throw new InvalidOperationException("Image inspection must not start a process.");
            public Task<ProcessExecutionResult> RunAsync(string fileName, IEnumerable<string> arguments, string workingDirectory, CancellationToken cancellationToken = default)
                => throw new InvalidOperationException("Image inspection must not start a process.");
            public Task<ProcessExecutionResult> RunAsync(string fileName, IEnumerable<string> arguments, string workingDirectory, Action<string>? onOutputData, Action<string>? onErrorData, CancellationToken cancellationToken = default)
                => throw new InvalidOperationException("Image inspection must not start a process.");
        }

        private sealed class PipelineImageInfoReader(PipelineFixture fixture) : IWindowsImageInfoReader
        {
            public Task<IReadOnlyList<WindowsImageInfo>> ReadAsync(string imagePath, CancellationToken cancellationToken = default)
            {
                Assert.True(File.Exists(imagePath));
                fixture.Events.Add("inspect");
                if (fixture.Failure == "native_image_error")
                    throw new COMException("Native image inspection failed.", unchecked((int)0x8007000D));
                var image = new WindowsImageInfo(1, "Windows 11 Pro", fixture.Failure == "missing_edition" ? "Core" : "Professional",
                    fixture.Failure == "invalid_size" ? 0UL : 10UL, "x64", new Version(10, 0, 26200));
                var images = new List<WindowsImageInfo> { image };
                if (fixture.Failure == "ambiguous_edition")
                    images.Add(image with { Index = 2 });
                else if (fixture.OptionalFeatureId is not null)
                    images.Add(image with { Index = 2, Name = "Windows Setup Media", EditionId = "", SizeBytes = 100 });
                return Task.FromResult<IReadOnlyList<WindowsImageInfo>>(images);
            }
        }

    }

    private sealed class FakeStorage : IDeploymentStorageService
    {
        public long? AvailableBytes { get; set; } = 64L * 1024 * 1024 * 1024;
        public bool Writable { get; set; } = true;
        public long? TargetAvailableBytes { get; set; } = 64L * 1024 * 1024 * 1024;
        public long? GetAvailableBytes(string path) => path.Contains("Windows", StringComparison.Ordinal) ? TargetAvailableBytes : AvailableBytes;
        public bool CanWriteDirectory(string path, string? existingFilePath = null) => Writable;
    }
}
