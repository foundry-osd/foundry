// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Security.Cryptography;
using Foundry.Deploy.Models;
using Foundry.Deploy.Services.Cache;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.Download;

namespace Foundry.Deploy.Tests;

public sealed class DeploymentPreflightServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VerifiedExistingBytes_AreReusedWithoutWriteProbeOrTransfer(bool legacy)
    {
        using var fixture = new Fixture();
        string path = fixture.CachePath(legacy);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, Fixture.Payload);
        fixture.StorageStatus = new(true, false, 0);

        DeploymentPreflightResult result = await fixture.Prepare();

        Assert.Equal(ImagePreflightLevel.CompleteImageVerified, result.Level);
        Assert.Equal(path, result.VerifiedImagePath);
        Assert.Same(fixture.Request.OperatingSystem, result.Selection);
        Assert.NotNull(result.Image);
        Assert.Equal(new[] { "hash", "inspect" }, fixture.Events);
        Assert.True(fixture.LeaseObserved);
    }

    [Fact]
    public async Task IndependentCapacity_AllowsAcquisitionThenLockedReverification()
    {
        using var fixture = new Fixture();
        DeploymentPreflightResult result = await fixture.Prepare();

        Assert.Equal(ImagePreflightLevel.CompleteImageVerified, result.Level);
        Assert.Equal(new[] { "storage", "download", "hash", "inspect" }, fixture.Events);
        Assert.True(fixture.LeaseObserved);
    }

    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(true, false, 1000000000)]
    [InlineData(true, true, 1)]
    [InlineData(true, true, -1)]
    public async Task UnavailableIndependentCapacity_UsesExplicitMetadataOnlyResult(bool present, bool writable, long free)
    {
        using var fixture = new Fixture();
        fixture.StorageStatus = new(present, writable, free < 0 ? null : free);

        DeploymentPreflightResult result = await fixture.Prepare();

        Assert.Equal(ImagePreflightLevel.TargetBackedMetadataOnly, result.Level);
        Assert.Null(result.VerifiedImagePath);
        Assert.Null(result.Image);
        Assert.False(string.IsNullOrWhiteSpace(result.ConstraintReason));
        Assert.Same(fixture.Request.OperatingSystem, result.Selection);
        Assert.Equal(new[] { "storage", "availability" }, fixture.Events);
    }

    [Fact]
    public async Task NoIndependentRoot_DoesNotInspectOrCreateTargetBackedStorage()
    {
        using var fixture = new Fixture();
        DeploymentPreflightResult result = await fixture.Prepare(independent: false);

        Assert.Equal(ImagePreflightLevel.TargetBackedMetadataOnly, result.Level);
        Assert.Equal(new[] { "availability" }, fixture.Events);
        Assert.False(Directory.Exists(fixture.CacheRoot));
    }

    [Fact]
    public async Task UnknownCompressedSize_DoesNotStartSpeculativeTransferOrProbe()
    {
        using var fixture = new Fixture();
        fixture.Request = fixture.Request with { OperatingSystem = fixture.Request.OperatingSystem with { SizeBytes = 0 } };

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Prepare());
        Assert.Empty(fixture.Events);
    }

    [Fact]
    public async Task TooSmallTarget_IsRejectedBeforeFreshTransfer()
    {
        using var fixture = new Fixture();
        fixture.Target = fixture.Target with { SizeBytes = 32UL * 1024 * 1024 * 1024 };

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Prepare());
        Assert.DoesNotContain("download", fixture.Events);
        Assert.DoesNotContain("availability", fixture.Events);
    }

    [Fact]
    public async Task AcquisitionFailure_DoesNotFallBackToMetadataOnly()
    {
        using var fixture = new Fixture();
        fixture.AcquisitionError = new IOException("controlled transfer failure");

        Assert.Same(fixture.AcquisitionError, await Assert.ThrowsAsync<IOException>(() => fixture.Prepare()));
        Assert.DoesNotContain("availability", fixture.Events);
        Assert.DoesNotContain("inspect", fixture.Events);
    }

    [Theory]
    [InlineData("edition")]
    [InlineData("architecture")]
    [InlineData("build")]
    [InlineData("language")]
    [InlineData("size")]
    public async Task ActualImageMismatch_IsRejected(string field)
    {
        using var fixture = new Fixture();
        fixture.Image = field switch
        {
            "edition" => fixture.Image with { EditionId = "Core" },
            "architecture" => fixture.Image with { Architecture = "x86" },
            "build" => fixture.Image with { Version = new(10, 0, 26100, 2) },
            "size" => fixture.Image with { ExpandedSizeBytes = 0 },
            _ => fixture.Image with { DefaultLanguage = "fr-FR" }
        };

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Prepare());
        Assert.DoesNotContain("availability", fixture.Events);
    }

    [Theory]
    [InlineData("release")]
    [InlineData("build")]
    [InlineData("edition")]
    [InlineData("language")]
    [InlineData("digest")]
    public async Task UnsupportedSelection_FailsBeforeStorageOrNetwork(string field)
    {
        using var fixture = new Fixture();
        OperatingSystemCatalogItem selection = fixture.Request.OperatingSystem;
        fixture.Request = fixture.Request with
        {
            OperatingSystem = field switch
            {
                "release" => selection with { WindowsRelease = "10" },
                "build" => selection with { BuildMajor = 25000 },
                "edition" => selection with { Edition = "Unknown" },
                "language" => selection with { LanguageCode = "en" },
                _ => selection with { Sha256 = string.Empty }
            }
        };

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Prepare());
        Assert.Empty(fixture.Events);
    }

    [Fact]
    public async Task UnselectedDriverBytes_AreNotReservedButSelectedBytesAre()
    {
        using var fixture = new Fixture();
        fixture.Request = fixture.Request with { DriverPack = new DriverPackCatalogItem { SizeBytes = 5L * 1024 * 1024 * 1024 } };
        DeploymentPreflightResult unselected = await fixture.Prepare(independent: false);
        fixture.Request = fixture.Request with { DriverPackSelectionKind = DriverPackSelectionKind.OemCatalog };
        DeploymentPreflightResult selected = await fixture.Prepare(independent: false);

        Assert.Equal(5L * 1024 * 1024 * 1024, selected.RequiredTargetBytes - unselected.RequiredTargetBytes);
    }

    [Fact]
    public async Task CallerCancellation_StopsBeforeAnyWork()
    {
        using var fixture = new Fixture();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.PrepareAsync(
            fixture.Request, fixture.Target, fixture.CacheRoot, fixture.Root, new CancellationToken(true)));
        Assert.Empty(fixture.Events);
    }

    private sealed class Fixture : IArtifactDownloadService, IWindowsImageInspectionService, IArtifactAvailabilityProbe, IVolumeStorageProbe, IDisposable
    {
        public static readonly byte[] Payload = "a controlled image fixture"u8.ToArray();
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "Foundry-PreflightTests", Guid.NewGuid().ToString("N"));
        public string CacheRoot => Path.Combine(Root, "cache");
        public List<string> Events { get; } = [];
        public bool LeaseObserved { get; private set; }
        public Exception? AcquisitionError { get; set; }
        public VolumeStorageStatus StorageStatus { get; set; } = new(true, true, 1024L * 1024 * 1024);
        public WindowsImageInfo Image { get; set; } = new(1, "Professional", "x64", new Version(10, 0, 26100, 1), "en-US", 24L * 1024 * 1024 * 1024);
        public TargetDiskInfo Target { get; set; } = new() { SizeBytes = 256UL * 1024 * 1024 * 1024 };
        public DeploymentContext Request { get; set; } = new()
        {
            Mode = DeploymentMode.Iso,
            CacheRootPath = "unused",
            TargetDiskNumber = 1,
            TargetComputerName = "TEST",
            DriverPackSelectionKind = DriverPackSelectionKind.None,
            OperatingSystem = new()
            {
                CatalogRevision = "test-revision",
                SourceId = "image",
                WindowsRelease = "11",
                ReleaseId = "24H2",
                BuildMajor = 26100,
                BuildUbr = 1,
                Build = "26100.1",
                Architecture = "x64",
                Edition = "Pro",
                LicenseChannel = "RET",
                LanguageCode = "en-US",
                FileName = "install.esd",
                Url = "https://example.test/install.esd",
                SizeBytes = Payload.Length,
                Sha256 = Convert.ToHexString(SHA256.HashData(Payload))
            }
        };
        public DeploymentPreflightService Service => new(this, this, this, this);
        public Task<DeploymentPreflightResult> Prepare(bool independent = true)
            => Service.PrepareAsync(Request, Target, independent ? CacheRoot : null, Root);
        public string CachePath(bool legacy = false) => legacy ? Path.Combine(CacheRoot, Request.OperatingSystem.FileName)
            : Path.Combine(CacheRoot, ArtifactIntegrityPolicy.FromOperatingSystem(Request.OperatingSystem).CacheKey, Request.OperatingSystem.FileName);
        public async Task<ArtifactDownloadResult?> TryUseCachedAsync(ArtifactIdentity artifact, string path, CancellationToken cancellationToken = default)
        {
            Events.Add("hash");
            Assert.Throws<IOException>(() => File.Open(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite).Dispose());
            byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            return Convert.ToHexString(SHA256.HashData(bytes)) == artifact.Integrity.Digest!.Hex
                ? new() { DestinationPath = path, Downloaded = false, Method = "fake", SizeBytes = bytes.Length } : null;
        }
        public async Task<ArtifactDownloadResult> DownloadAsync(ArtifactIdentity artifact, string path, CancellationToken cancellationToken = default, IProgress<DownloadProgress>? progress = null)
        {
            Events.Add("download");
            if (AcquisitionError is not null) throw AcquisitionError;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, Payload, cancellationToken);
            return new() { DestinationPath = path, Downloaded = true, Method = "fake", SizeBytes = Payload.Length };
        }
        public Task<WindowsImageInfo> InspectImageAsync(string imagePath, OperatingSystemCatalogItem selection, string workingDirectory, CancellationToken cancellationToken = default)
        {
            Events.Add("inspect");
            Assert.Throws<IOException>(() => File.Open(imagePath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite).Dispose());
            LeaseObserved = true;
            return Task.FromResult(Image);
        }
        public Task EnsureAvailableAsync(ArtifactIdentity artifact, CancellationToken cancellationToken = default)
        { Events.Add("availability"); return Task.CompletedTask; }
        public VolumeStorageStatus Inspect(string directory) { Events.Add("storage"); return StorageStatus; }
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
}
