// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Core.Models.PreOobe;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.Packages;
using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPePreOobeMediaTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "Fmedia", Guid.NewGuid().ToString("N"));
    private CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PublicationAllowsLongHostStagingPathsAndReusesVerifiedContent()
    {
        var (library, settings) = await InputsAsync();
        var publisher = new WinPePreOobeMediaService(_ => long.MaxValue);
        using var package = await publisher.PrepareAsync(library, settings, Cancellation);
        string media = Path.Combine(root, "Workspaces", Guid.NewGuid().ToString("N"), "Scratch", "Iso",
            "custom-iso-" + Guid.NewGuid().ToString("N"), "media");
        Assert.All(package.Files, file => Assert.True(Path.Combine(media, file.RelativePath).Length > PreOobePackagePathPolicy.MaximumFullPathLength));

        await publisher.PublishAsync(package, media, Cancellation);

        Assert.All(package.Files, file => Assert.Equal(File.ReadAllBytes(file.SourcePath), File.ReadAllBytes(Path.Combine(media, file.RelativePath))));
        Assert.True(Directory.Exists(Path.Combine(media, package.Manifest.Packages[0].RelativePath, "empty")));
        Assert.Equal(package.ManifestBytes, await File.ReadAllBytesAsync(Path.Combine(media, package.ManifestRelativePath), Cancellation));
        Assert.Equal(WinPePreOobeMediaService.ReserveBytes + package.ManifestBytes.Length,
            await publisher.GetRequiredBytesAsync(package, media, Cancellation));
        Assert.Empty(Directory.GetDirectories(Path.Combine(media, "Cache", "PreOobe"), ".pending-*"));
    }

    [Fact]
    public async Task GenerationDeduplicatesPackagesPreservesEmptyDirectories()
    {
        var (library, settings) = await InputsAsync();
        var publisher = new WinPePreOobeMediaService(_ => long.MaxValue);
        using var package = await publisher.PrepareAsync(library, settings with { Actions = [settings.Actions[0], settings.Actions[0] with { Id = Guid.NewGuid().ToString("N") }] }, Cancellation);
        string media = Path.Combine(root, "media");
        await publisher.PublishAsync(package, media, Cancellation);
        Assert.Single(package.Manifest.Packages);
        Assert.Equal($"Cache/PreOobe/Packages/{settings.Actions[0].Package!.ContentHash}/files", package.Manifest.Packages[0].RelativePath);
        Assert.Single(package.Files);
        Assert.True(File.Exists(Path.Combine(media, package.ManifestRelativePath)));
        Assert.True(Directory.Exists(Path.Combine(media, package.Manifest.Packages[0].RelativePath, "empty")));
        Assert.All(package.Files, file => Assert.True(File.Exists(Path.Combine(media, file.RelativePath))));
        long reused = await publisher.GetRequiredBytesAsync(package, media, Cancellation);
        Assert.Equal(WinPePreOobeMediaService.ReserveBytes + package.ManifestBytes.Length, reused);
    }

    [Fact]
    public async Task CancellationKeepsPriorGenerationAndUnmanagedContent()
    {
        var (library, settings) = await InputsAsync();
        var publisher = new WinPePreOobeMediaService(_ => long.MaxValue);
        using var prior = await publisher.PrepareAsync(library, settings, Cancellation);
        string media = Path.Combine(root, "media");
        await publisher.PublishAsync(prior, media, Cancellation);
        string manual = Path.Combine(media, "Cache", "PreOobe", "operator.txt");
        await File.WriteAllTextAsync(manual, "preserve", Cancellation);
        using var next = await publisher.PrepareAsync(library, settings, Cancellation);
        using var canceled = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        var progress = new CallbackProgress(_ => canceled.Cancel());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => publisher.PublishAsync(next, media, canceled.Token, progress));
        Assert.True(File.Exists(Path.Combine(media, prior.ManifestRelativePath)));
        Assert.False(File.Exists(Path.Combine(media, next.ManifestRelativePath)));
        Assert.Equal("preserve", await File.ReadAllTextAsync(manual, Cancellation));
        Assert.Empty(Directory.GetDirectories(Path.Combine(media, "Cache", "PreOobe"), ".pending-*"));
    }

    [Fact]
    public async Task CapacityAndConfigurationFailuresOccurBeforePublication()
    {
        var (library, settings) = await InputsAsync();
        var publisher = new WinPePreOobeMediaService(_ => 0);
        using var package = await publisher.PrepareAsync(library, settings, Cancellation);
        var generator = new DeployConfigurationGenerator();
        string config = generator.Serialize(generator.Generate(new() { PreOobe = settings }, null, null, null, "26.10.3.1"));
        string bound = WinPePreOobeMediaService.BindConfiguration(package, config);
        using JsonDocument boundJson = JsonDocument.Parse(bound);
        Assert.Equal("26.10.3.1", boundJson.RootElement.GetProperty("authoringVersion").GetString());
        WinPePreOobeMediaService.ValidateConfigurationBinding(package, bound);
        Assert.Throws<InvalidDataException>(() => WinPePreOobeMediaService.ValidateConfigurationBinding(package, config));
        string media = Path.Combine(root, "media");
        await Assert.ThrowsAsync<IOException>(() => publisher.PublishAsync(package, media, Cancellation));
        Assert.False(File.Exists(Path.Combine(media, package.ManifestRelativePath)));
    }

    [Fact]
    public async Task MissingEntryPointIsRejectedAndDisabledContentIsNotImported()
    {
        var (library, settings) = await InputsAsync();
        var publisher = new WinPePreOobeMediaService(_ => long.MaxValue);
        await Assert.ThrowsAsync<InvalidDataException>(() => publisher.PrepareAsync(library,
            settings with { Actions = [settings.Actions[0] with { EntryPoint = "missing.ps1" }] }, Cancellation));
        using var package = await publisher.PrepareAsync(library, settings with { Actions = [settings.Actions[0] with { IsEnabled = false }] }, Cancellation);
        Assert.Empty(package.Manifest.Packages);
        Assert.Empty(package.Files);
    }

    [Fact]
    public async Task ManifestBytesContainTheSameIdentityBoundToConfiguration()
    {
        var (library, settings) = await InputsAsync();
        using var package = await new WinPePreOobeMediaService().PrepareAsync(library, settings, Cancellation);
        var decoded = JsonSerializer.Deserialize<PreOobeMediaManifest>(package.ManifestBytes, ConfigurationJsonDefaults.SerializerOptions)!;
        Assert.Equal(package.ManifestId, decoded.Id);
        Assert.Equal(settings.Actions[0].Package!.ContentHash, Assert.Single(decoded.Packages).ContentHash);
    }

    private async Task<(PreOobePackageLibraryService Library, PreOobeSettings Settings)> InputsAsync()
    {
        string source = Path.Combine(root, "source");
        Directory.CreateDirectory(Path.Combine(source, "empty"));
        await File.WriteAllTextAsync(Path.Combine(source, "setup.ps1"), "exit 0", Cancellation);
        var library = new PreOobePackageLibraryService(Path.Combine(root, "library"));
        var reference = await library.ImportAsync(source, Cancellation);
        var action = PreOobeActionSettings.Create(PreOobeActionKind.PowerShell, "Setup") with { Package = reference, EntryPoint = "setup.ps1" };
        return (library, new() { IsEnabled = true, Actions = [action] });
    }

    private sealed class CallbackProgress(Action<WinPeMediaProgress> callback) : IProgress<WinPeMediaProgress>
    {
        public void Report(WinPeMediaProgress value) => callback(value);
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
}
