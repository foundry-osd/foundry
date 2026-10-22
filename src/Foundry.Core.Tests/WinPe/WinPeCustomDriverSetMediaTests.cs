// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.Drivers;
using Foundry.Core.Services.Packages;
using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeCustomDriverSetMediaTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "Fdrivers", Guid.NewGuid().ToString("N"));
    private CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task DriverSetsArePublishedUnderTheDriversMediaRoot()
    {
        var (library, settings) = await InputsAsync();

        using WinPeContentPackageMediaLease lease = await WinPeCustomDriverSetMediaService.PrepareAsync(library, settings, Cancellation);

        Assert.Equal(ContentPackageKind.Drivers, lease.Kind);
        Assert.NotEmpty(lease.Files);
        Assert.All(lease.Files, file => Assert.StartsWith("Cache/DriverPacks/Custom/Packages/", file.RelativePath, StringComparison.Ordinal));
        Assert.StartsWith("Cache/DriverPacks/Custom/manifests/", lease.ManifestRelativePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrepareIncludesOnlyIncludedSetsWithDistinctHashes()
    {
        var (library, settings) = await InputsAsync();
        CustomDriverSetReference included = settings.Sets[0];
        ContentPackageReference other = await ImportAsync(library, "other", "other.inf");
        CustomDriverSetsSettings mixed = settings with
        {
            Sets =
            [
                included,
                included with { Id = Guid.NewGuid().ToString("N"), IsIncluded = false },
                new() { Id = Guid.NewGuid().ToString("N"), DisplayName = "Excluded", Package = other, IsIncluded = false }
            ]
        };

        using WinPeContentPackageMediaLease lease = await WinPeCustomDriverSetMediaService.PrepareAsync(library, mixed, Cancellation);

        Assert.Equal(included.Package.ContentHash, Assert.Single(lease.Manifest.Packages).ContentHash);
    }

    [Fact]
    public async Task PrepareRejectsSettingsWithoutAnIncludedSet()
    {
        var (library, settings) = await InputsAsync();

        await Assert.ThrowsAsync<InvalidDataException>(() => WinPeCustomDriverSetMediaService.PrepareAsync(library,
            settings with { Sets = [settings.Sets[0] with { IsIncluded = false }] }, Cancellation));
        await Assert.ThrowsAsync<InvalidDataException>(() => WinPeCustomDriverSetMediaService.PrepareAsync(library,
            settings with { IsEnabled = false }, Cancellation));
    }

    [Fact]
    public async Task BindingWritesTheManifestIdentityIntoTheDriversSection()
    {
        var (library, settings) = await InputsAsync();
        using WinPeContentPackageMediaLease lease = await WinPeCustomDriverSetMediaService.PrepareAsync(library, settings, Cancellation);

        string bound = WinPeCustomDriverSetMediaService.BindConfiguration(lease, Configuration(settings));

        var document = JsonSerializer.Deserialize<FoundryDeployConfigurationDocument>(bound, ConfigurationJsonDefaults.SerializerOptions)!;
        Assert.Equal(lease.ManifestId, document.CustomDrivers.ManifestId);
        Assert.Equal(lease.ManifestHash, document.CustomDrivers.ManifestHash);
        WinPeCustomDriverSetMediaService.ValidateConfigurationBinding(lease, bound);
    }

    [Fact]
    public async Task BindingRejectsConfigurationWhoseHashesDifferFromTheManifest()
    {
        var (library, settings) = await InputsAsync();
        ContentPackageReference other = await ImportAsync(library, "other", "other.inf");
        using WinPeContentPackageMediaLease lease = await WinPeCustomDriverSetMediaService.PrepareAsync(library, settings, Cancellation);
        CustomDriverSetsSettings changed = settings with { Sets = [settings.Sets[0] with { Package = other }] };

        InvalidDataException mismatch = Assert.Throws<InvalidDataException>(() =>
            WinPeCustomDriverSetMediaService.BindConfiguration(lease, Configuration(changed)));
        Assert.Equal("CustomDrivers.InvalidMediaBinding", mismatch.Message);
        InvalidDataException unbound = Assert.Throws<InvalidDataException>(() =>
            WinPeCustomDriverSetMediaService.ValidateConfigurationBinding(lease, Configuration(settings)));
        Assert.Equal("CustomDrivers.InvalidMediaBinding", unbound.Message);
    }

    [Fact]
    public async Task DriversPublicationReportsTheDriversStatuses()
    {
        var (library, settings) = await InputsAsync();
        using WinPeContentPackageMediaLease lease = await WinPeCustomDriverSetMediaService.PrepareAsync(library, settings, Cancellation);
        var progress = new CollectingProgress();
        string media = Path.Combine(root, "media");

        await new WinPeContentPackageMediaService(ContentPackageKind.Drivers, _ => long.MaxValue).PublishAsync(lease, media, Cancellation, progress);

        Assert.Contains(progress.Reports, report => report.Status == WinPeProgressStatuses.StagingCustomDrivers);
        Assert.Equal(WinPeProgressStatuses.CustomDriversVerified, progress.Reports[^1].Status);
        Assert.DoesNotContain(progress.Reports, report => report.Status is WinPeProgressStatuses.StagingPostInstallation or WinPeProgressStatuses.PostInstallationVerified);
        Assert.True(File.Exists(Path.Combine(media, lease.ManifestRelativePath)));
    }

    [Fact]
    public async Task LibraryCountsInfFilesCaseInsensitively()
    {
        var library = new CustomDriverSetLibrary(Path.Combine(root, "library"));
        string source = Path.Combine(root, "mixed");
        Directory.CreateDirectory(Path.Combine(source, "sub"));
        await File.WriteAllTextAsync(Path.Combine(source, "a.inf"), "[Version]", Cancellation);
        await File.WriteAllTextAsync(Path.Combine(source, "sub", "B.INF"), "[Version]", Cancellation);
        await File.WriteAllTextAsync(Path.Combine(source, "c.sys"), "driver", Cancellation);
        ContentPackageReference reference = await library.ImportAsync(source, Cancellation);

        using ContentPackageLease lease = await library.AcquireAsync(reference.ContentHash, Cancellation);

        Assert.Equal(ContentPackageKind.Drivers, library.Library.Kind);
        Assert.True(library.IsAvailable(reference));
        Assert.Equal(2, CustomDriverSetLibrary.CountInfFiles(lease.Manifest));
    }

    [Fact]
    public async Task LibraryReadsTheInfCountWithoutALeaseAndReturnsNullForMissingContent()
    {
        var library = new CustomDriverSetLibrary(Path.Combine(root, "library"));
        ContentPackageReference reference = await ImportAsync(library, "counted", "net.inf");

        Assert.Equal(1, library.TryCountInfFiles(reference.ContentHash));
        Assert.Null(library.TryCountInfFiles(new string('a', 64)));
        Assert.Null(library.TryCountInfFiles("not-a-hash"));
    }

    private async Task<(CustomDriverSetLibrary Library, CustomDriverSetsSettings Settings)> InputsAsync()
    {
        var library = new CustomDriverSetLibrary(Path.Combine(root, "library"));
        ContentPackageReference reference = await ImportAsync(library, "source", "net.inf");
        var set = new CustomDriverSetReference { Id = Guid.NewGuid().ToString("N"), DisplayName = "Network", Package = reference };
        return (library, new() { IsEnabled = true, Sets = [set], DefaultSetId = set.Id });
    }

    private async Task<ContentPackageReference> ImportAsync(CustomDriverSetLibrary library, string name, string infName)
    {
        string source = Path.Combine(root, name);
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, infName), "[Version]\r\nSignature=\"$Windows NT$\"\r\n" + name, Cancellation);
        await File.WriteAllTextAsync(Path.Combine(source, name + ".sys"), "driver " + name, Cancellation);
        return await library.ImportAsync(source, Cancellation);
    }

    private static string Configuration(CustomDriverSetsSettings settings)
    {
        var generator = new DeployConfigurationGenerator();
        return generator.Serialize(generator.Generate(new() { CustomDrivers = settings }, null, null, null, "26.10.3.1"));
    }

    private sealed class CollectingProgress : IProgress<WinPeMediaProgress>
    {
        public List<WinPeMediaProgress> Reports { get; } = [];
        public void Report(WinPeMediaProgress value) => Reports.Add(value);
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
}
