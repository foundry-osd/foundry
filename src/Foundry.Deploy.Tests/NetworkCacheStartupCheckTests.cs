// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text;
using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Core.Services.Drivers;
using Foundry.Core.Services.Images;
using Foundry.Core.Services.NetworkCache;
using Foundry.Core.Services.Packages;
using Foundry.Core.Services.Sharing;
using Foundry.Core.Services.WinPe;
using Foundry.Deploy.Services.Cache;
using Foundry.Deploy.Services.Configuration;
using Foundry.Utilities.Networking;
using Microsoft.Extensions.Logging;
using FoundryDeployConfigurationDocument = Foundry.Deploy.Models.Configuration.FoundryDeployConfigurationDocument;

namespace Foundry.Deploy.Tests;

public sealed class NetworkCacheStartupCheckTests : IDisposable
{
    private const string ImageName = "Managed image";
    private const string DriverSetName = "Network drivers";
    private const string ActionName = "Install agent";

    private readonly string root = Path.Combine(Path.GetTempPath(), "Fcachecheck", Guid.NewGuid().ToString("N"));
    private readonly PublicationSigningKey signer = PublicationSigningKey.Create();
    private readonly RecordingLogger logger = new();

    private string Share => Path.Combine(root, "share");
    private string ContentRoot => Path.Combine(Share, "Foundry");
    private CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        signer.Dispose();
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_WithoutPublisherKey_ReturnsTheDocumentUnchanged(bool isEnabled)
    {
        // A key-less document never reads the share: even a failed session passes, as media without an index behaves as before.
        FoundryDeployConfigurationDocument document = new() { NetworkCache = new() { IsEnabled = isEnabled } };
        using NetworkCacheSession session = NetworkCacheSession.Unavailable(NetworkCacheOutcome.Unavailable,
            new NetworkCacheStartupFailure(NetworkCacheStartupFailureKind.Unreachable, @"\\srv\cache"));

        NetworkCacheStartupResult result = await CreateCheck().RunAsync(document, session, Cancellation);

        Assert.Same(document, result.Document);
        Assert.Null(result.Failure);
    }

    [Fact]
    public async Task RunAsync_WhenTheSessionFailed_ReturnsItsFailure()
    {
        FoundryDeployConfigurationDocument document = CreateDocument();
        var failure = new NetworkCacheStartupFailure(NetworkCacheStartupFailureKind.NoScope, DeviceAddress: "10.20.5.14");
        using NetworkCacheSession session = NetworkCacheSession.Unavailable(NetworkCacheOutcome.NoMatchingScope, failure);

        NetworkCacheStartupResult result = await CreateCheck().RunAsync(document, session, Cancellation);

        Assert.Same(document, result.Document);
        Assert.Same(failure, result.Failure);
    }

    [Fact]
    public async Task RunAsync_WhenTheIndexIsMissing_ReportsNotPublished()
    {
        Directory.CreateDirectory(ContentRoot);

        NetworkCacheStartupResult result = await RunAsync(CreateDocument());

        AssertNotPublished(result, NetworkCacheIndexReadStatus.Missing);
    }

    [Fact]
    public async Task RunAsync_IgnoresTemporaryIndexFiles()
    {
        // An interrupted publication leaves a complete, validly signed index under a temporary name; it must never be read.
        byte[] valid = NetworkCacheContentIndexCodec.Write(CreateIndex(), signer);
        string folder = Directory.CreateDirectory(Path.Combine(ContentRoot, NetworkCacheIndexStore.FolderName)).FullName;
        await File.WriteAllBytesAsync(Path.Combine(folder, $"{signer.KeyId}.json.{Guid.NewGuid():N}{NetworkCacheIndexStore.TemporarySuffix}"), valid, Cancellation);

        NetworkCacheStartupResult result = await RunAsync(CreateDocument());

        AssertNotPublished(result, NetworkCacheIndexReadStatus.Missing);
    }

    [Theory]
    [InlineData(NetworkCacheIndexReadStatus.SignatureInvalid)]
    [InlineData(NetworkCacheIndexReadStatus.UnexpectedKey)]
    [InlineData(NetworkCacheIndexReadStatus.TooOld)]
    [InlineData(NetworkCacheIndexReadStatus.Malformed)]
    public async Task RunAsync_WhenTheIndexIsNotTrusted_ReportsNotPublishedWithTheStatus(NetworkCacheIndexReadStatus status)
    {
        using PublicationSigningKey other = PublicationSigningKey.Create();
        byte[] file = status switch
        {
            NetworkCacheIndexReadStatus.SignatureInvalid => NetworkCacheContentIndexCodec.Write(CreateIndex() with { KeyId = other.KeyId }, other),
            NetworkCacheIndexReadStatus.UnexpectedKey => SignedWithKeyId(CreateIndex() with { KeyId = other.KeyId }),
            NetworkCacheIndexReadStatus.TooOld => NetworkCacheContentIndexCodec.Write(CreateIndex() with { Sequence = 4 }, signer),
            _ => Encoding.UTF8.GetBytes("not an index")
        };
        await NetworkCacheIndexStore.WriteAsync(ContentRoot, signer.KeyId, file, Cancellation);

        NetworkCacheStartupResult result = await RunAsync(CreateDocument(minimumSequence: 5));

        AssertNotPublished(result, status);
    }

    [Fact]
    public async Task RunAsync_WhenTheIndexPostInstallationSectionIsInvalid_ReportsNotPublished()
    {
        // The boot image loader rejects such a section; the index must not bring it in past that rule.
        await PublishIndexAsync(CreateIndex() with
        {
            PreOobe = new() { IsEnabled = true, Actions = [new() { Name = "", Kind = PreOobeActionKind.Restart }] }
        });

        NetworkCacheStartupResult result = await RunAsync(CreateDocument());

        Assert.Equal(NetworkCacheStartupFailureKind.NotPublished, result.Failure?.Kind);
        Assert.Empty(result.Document.PreOobe.Actions);
        LogEntry entry = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Equal("PreOobe.InvalidActionName", entry.Properties["ValidationCode"]);
    }

    [Fact]
    public async Task RunAsync_WithAnEmptyIndex_AppliesTheEmptySections()
    {
        // Network cache on without any imported content: the published index has empty sections and the check passes.
        await PublishIndexAsync(CreateIndex());
        FoundryDeployConfigurationDocument document = CreateDocument() with
        {
            CustomImages = new() { IsEnabled = true, ManifestId = "stale" },
            PreOobe = new() { IsEnabled = true }
        };

        NetworkCacheStartupResult result = await RunAsync(document);

        Assert.Null(result.Failure);
        Assert.Equal(new DeployCustomImagesSettings(), result.Document.CustomImages);
        Assert.False(result.Document.PreOobe.IsEnabled);
        Assert.Empty(result.Document.PreOobe.Actions);
        Assert.Empty(result.Document.CustomDrivers.Sets);
    }

    [Fact]
    public async Task RunAsync_WhenEveryFileIsPresent_AppliesTheIndexSections()
    {
        NetworkCacheContentIndex index = await PublishContentAsync();
        await PublishIndexAsync(index);
        FoundryDeployConfigurationDocument document = CreateDocument() with { Completion = new() { AutomaticRebootDelaySeconds = 42 } };

        NetworkCacheStartupResult result = await RunAsync(document);

        Assert.Null(result.Failure);
        Assert.Equal(Json(index.CustomImages), Json(result.Document.CustomImages));
        Assert.Equal(Json(index.CustomDrivers), Json(result.Document.CustomDrivers));
        Assert.Equal(Json(index.PreOobe), Json(result.Document.PreOobe));
        Assert.Equal(document.NetworkCache, result.Document.NetworkCache);
        Assert.Equal(42, result.Document.Completion.AutomaticRebootDelaySeconds);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Properties.ContainsKey("Sequence"));
    }

    [Fact]
    public async Task RunAsync_WhenTheManifestsAreMissing_NamesTheContentThatNeedsThem()
    {
        NetworkCacheContentIndex index = await PublishContentAsync();
        Directory.Delete(Path.Combine(ContentRoot, "Cache"), recursive: true);
        await PublishIndexAsync(index);

        NetworkCacheStartupResult result = await RunAsync(CreateDocument());

        AssertContentMissing(result, NetworkCacheContentPresence.CustomImagesItem, DriverSetName, ActionName);
    }

    [Fact]
    public async Task RunAsync_WhenAListedFileIsMissingOrHasAnotherLength_NamesItsContent()
    {
        NetworkCacheContentIndex index = await PublishContentAsync();
        await PublishIndexAsync(index);
        await File.AppendAllTextAsync(Single("image.wim"), "changed", Cancellation);
        File.Delete(Single("net.inf"));
        await File.WriteAllTextAsync(Single("agent.ps1"), "", Cancellation);

        NetworkCacheStartupResult result = await RunAsync(CreateDocument());

        AssertContentMissing(result, ImageName, DriverSetName, ActionName);
    }

    [Fact]
    public async Task RunAsync_WhenCancelled_Throws()
    {
        await PublishIndexAsync(CreateIndex());
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        using NetworkCacheSession session = NetworkCacheSession.Connected(Share, canWrite: false, new NoopConnector());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateCheck().RunAsync(CreateDocument(), session, cancelled.Token));
    }

    [Fact(Timeout = 30000)]
    public async Task RunAsync_WhenThePresenceCheckStalls_ReportsTheShareUnreachableAfterTheTimeout()
    {
        await PublishIndexAsync(CreateIndex());
        // A stalled SMB call ignores cancellation, so the check must stop waiting on its own.
        var stalled = new TaskCompletionSource<IReadOnlyList<string>>();
        var check = new NetworkCacheStartupCheck(logger, TimeSpan.FromMilliseconds(100), (_, _, _) => stalled.Task);
        using NetworkCacheSession session = NetworkCacheSession.Connected(Share, canWrite: false, new NoopConnector());

        NetworkCacheStartupResult result = await check.RunAsync(CreateDocument(), session, TestContext.Current.CancellationToken);

        Assert.Equal(NetworkCacheStartupFailureKind.Unreachable, result.Failure?.Kind);
        Assert.Equal(Share, result.Failure!.SharePath);
        Assert.Null(result.Document.CustomImages.ManifestId);
        Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    [Fact(Timeout = 30000)]
    public async Task RunAsync_WhenTheIndexReadStalls_ReportsTheShareUnreachableAfterTheTimeout()
    {
        var stalled = new TaskCompletionSource<byte[]?>();
        var check = new NetworkCacheStartupCheck(logger, TimeSpan.FromMilliseconds(100),
            (_, _, _) => throw new InvalidOperationException("The presence check must not run."), (_, _, _) => stalled.Task);
        using NetworkCacheSession session = NetworkCacheSession.Connected(Share, canWrite: false, new NoopConnector());

        NetworkCacheStartupResult result = await check.RunAsync(CreateDocument(), session, TestContext.Current.CancellationToken);

        Assert.Equal(NetworkCacheStartupFailureKind.Unreachable, result.Failure?.Kind);
        Assert.Equal(Share, result.Failure!.SharePath);
        Assert.True(result.Failure.CanRetry);
        Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task RunAsync_WhenTheShareRefusesAReadAfterConnecting_ReportsARetryableAccessDenied()
    {
        // The session connected, so only the server permissions refuse the read; a retry after they are fixed can pass.
        NetworkCacheStartupResult result = await RunAsync(CreateFailingCheck(new UnauthorizedAccessException()), CreateDocument());

        Assert.Equal(NetworkCacheStartupFailureKind.AccessDenied, result.Failure?.Kind);
        Assert.Equal(Share, result.Failure!.SharePath);
        Assert.True(result.Failure.DeniedAfterConnection);
        Assert.True(result.Failure.CanRetry);
        Assert.False(new NetworkCacheStartupFailure(NetworkCacheStartupFailureKind.AccessDenied, Share).CanRetry);
    }

    [Fact]
    public async Task RunAsync_WhenTheShareReadFailsUnexpectedly_ReportsARetryableNotPublishedWithTheReason()
    {
        // A damaged file must reach the blocking dialog instead of faulting the startup.
        NetworkCacheStartupResult result = await RunAsync(CreateFailingCheck(new InvalidDataException()), CreateDocument());

        Assert.Equal(NetworkCacheStartupFailureKind.NotPublished, result.Failure?.Kind);
        Assert.True(result.Failure!.CanRetry);
        LogEntry entry = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Equal(nameof(InvalidDataException), entry.Properties["Reason"]);
    }

    private NetworkCacheStartupCheck CreateCheck() => new(logger);

    private NetworkCacheStartupCheck CreateFailingCheck(Exception exception) =>
        new(logger, NetworkCacheStartupCheck.DefaultShareReadTimeout,
            (_, _, _) => throw new InvalidOperationException("The presence check must not run."), (_, _, _) => Task.FromException<byte[]?>(exception));

    private async Task<NetworkCacheStartupResult> RunAsync(NetworkCacheStartupCheck check, FoundryDeployConfigurationDocument document)
    {
        using NetworkCacheSession session = NetworkCacheSession.Connected(Share, canWrite: false, new NoopConnector());
        return await check.RunAsync(document, session, Cancellation);
    }

    private async Task<NetworkCacheStartupResult> RunAsync(FoundryDeployConfigurationDocument document)
    {
        using NetworkCacheSession session = NetworkCacheSession.Connected(Share, canWrite: false, new NoopConnector());
        return await CreateCheck().RunAsync(document, session, Cancellation);
    }

    private void AssertNotPublished(NetworkCacheStartupResult result, NetworkCacheIndexReadStatus status)
    {
        Assert.Equal(NetworkCacheStartupFailureKind.NotPublished, result.Failure?.Kind);
        Assert.Equal(Share, result.Failure!.SharePath);
        Assert.Null(result.Document.CustomImages.ManifestId);
        LogEntry entry = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Equal(status.ToString(), entry.Properties["IndexStatus"]?.ToString());
        Assert.Equal(signer.KeyId, entry.Properties["KeyId"]);
    }

    private void AssertContentMissing(NetworkCacheStartupResult result, params string[] items)
    {
        Assert.Equal(NetworkCacheStartupFailureKind.ContentMissing, result.Failure?.Kind);
        Assert.Equal(Share, result.Failure!.SharePath);
        Assert.Equal(items, result.Failure.MissingItems);
        Assert.Null(result.Document.CustomImages.ManifestId);
        LogEntry entry = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Equal(items.Length, entry.Properties["MissingCount"]);
    }

    private static string Json<T>(T value) => JsonSerializer.Serialize(value, ConfigurationJsonDefaults.SerializerOptions);

    private string Single(string fileName) =>
        Assert.Single(Directory.GetFiles(Path.Combine(ContentRoot, "Cache"), fileName, SearchOption.AllDirectories));

    private FoundryDeployConfigurationDocument CreateDocument(long minimumSequence = 0) => new()
    {
        NetworkCache = new()
        {
            IsEnabled = true,
            PublisherPublicKey = Convert.ToBase64String(signer.PublicKey),
            PublisherKeyId = signer.KeyId,
            MinimumIndexSequence = minimumSequence
        }
    };

    private NetworkCacheContentIndex CreateIndex() => new() { KeyId = signer.KeyId, Sequence = 7, PublishedAtUtc = DateTimeOffset.UtcNow };

    private Task PublishIndexAsync(NetworkCacheContentIndex index) =>
        NetworkCacheIndexStore.WriteAsync(ContentRoot, signer.KeyId, NetworkCacheContentIndexCodec.Write(index, signer), Cancellation);

    /// <summary>Builds an envelope whose signature verifies with the trusted key although the payload names another key.</summary>
    private byte[] SignedWithKeyId(NetworkCacheContentIndex index)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(index, ConfigurationJsonDefaults.SerializerOptions);
        return JsonSerializer.SerializeToUtf8Bytes(new { payload, signature = signer.Sign(payload) });
    }

    /// <summary>Publishes one image, one driver set and one post-installation package to the share and returns the matching index.</summary>
    private async Task<NetworkCacheContentIndex> PublishContentAsync()
    {
        string sources = Directory.CreateDirectory(Path.Combine(root, "sources")).FullName;

        string wim = Path.Combine(sources, "source.wim");
        await File.WriteAllTextAsync(wim, "image", Cancellation);
        var images = new CustomImageLibraryService(Path.Combine(root, "images"), new MetadataReader());
        CustomImageReference image = await images.ImportAsync(new(wim, ImageName), cancellationToken: Cancellation);
        using WinPeCustomImageMediaLease imageLease = await new WinPeCustomImageMediaService().PrepareAsync(images,
            new() { IsEnabled = true, Images = [image] }, Cancellation);
        await new WinPeCustomImageMediaService().PublishAsync(imageLease, ContentRoot, Cancellation);

        string driverSource = Directory.CreateDirectory(Path.Combine(sources, "drivers")).FullName;
        await File.WriteAllTextAsync(Path.Combine(driverSource, "net.inf"), "[Version]", Cancellation);
        await File.WriteAllTextAsync(Path.Combine(driverSource, "net.sys"), "driver", Cancellation);
        var drivers = new CustomDriverSetLibrary(Path.Combine(root, "drivers"));
        ContentPackageReference driverPackage = await drivers.ImportAsync(driverSource, Cancellation);
        using WinPeContentPackageMediaLease driverLease = await WinPeCustomDriverSetMediaService.PrepareAsync(drivers,
            new() { IsEnabled = true, Sets = [new() { Id = "set", DisplayName = DriverSetName, Package = driverPackage }] }, Cancellation);
        await new WinPeContentPackageMediaService(ContentPackageKind.Drivers).PublishAsync(driverLease, ContentRoot, Cancellation);

        string script = Path.Combine(sources, "agent.ps1");
        await File.WriteAllTextAsync(script, "exit 0", Cancellation);
        var packages = new ContentPackageLibraryService(Path.Combine(root, "packages"), ContentPackageKind.PostInstallation);
        ContentPackageReference package = await packages.ImportAsync(script, Cancellation);
        var preOobe = new PreOobeSettings
        {
            IsEnabled = true,
            Actions = [new() { Name = ActionName, Kind = PreOobeActionKind.PowerShell, Package = package, EntryPoint = "agent.ps1", Process = new() }]
        };
        using WinPeContentPackageMediaLease packageLease = await new WinPePreOobeMediaService().PrepareAsync(packages, preOobe, Cancellation);
        await new WinPeContentPackageMediaService(ContentPackageKind.PostInstallation).PublishAsync(packageLease, ContentRoot, Cancellation);

        return CreateIndex() with
        {
            CustomImages = new() { IsEnabled = true, ManifestId = imageLease.ManifestId, ManifestHash = imageLease.ManifestHash, DefaultImageId = image.Id },
            CustomDrivers = new()
            {
                IsEnabled = true,
                ManifestId = driverLease.ManifestId,
                ManifestHash = driverLease.ManifestHash,
                Sets = [new() { Id = "set", DisplayName = DriverSetName, ContentHash = driverPackage.ContentHash, Length = driverPackage.Length, FileCount = driverPackage.FileCount }]
            },
            PreOobe = new() { IsEnabled = true, Actions = preOobe.Actions, ManifestId = packageLease.ManifestId, ManifestHash = packageLease.ManifestHash }
        };
    }

    private sealed record LogEntry(LogLevel Level, IReadOnlyDictionary<string, object?> Properties);

    private sealed class RecordingLogger : ILogger<NetworkCacheStartupCheck>
    {
        public List<LogEntry> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var properties = state as IEnumerable<KeyValuePair<string, object?>> ?? [];
            lock (Entries) Entries.Add(new(logLevel, properties.ToDictionary(pair => pair.Key, pair => pair.Value)));
        }
    }

    private sealed class NoopConnector : ISmbConnector
    {
        public SmbConnectResult Connect(string uncPath, string? userName, ReadOnlySpan<char> password) => new(SmbConnectStatus.Connected, 0);
        public void Disconnect(string uncPath) { }
    }

    private sealed class MetadataReader : ICustomImageMetadataReader
    {
        public Task<IReadOnlyList<CustomImageIndex>> ReadAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CustomImageIndex>>([new() { Index = 1, Name = "Image", ExpandedSizeBytes = 1024 }]);
    }
}
