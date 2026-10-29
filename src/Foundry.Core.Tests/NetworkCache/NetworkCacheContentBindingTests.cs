// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.NetworkCache;
using Foundry.Core.Services.Packages;
using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.NetworkCache;

public sealed class NetworkCacheContentBindingTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "Fnc", Guid.NewGuid().ToString("N"));
    private CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task BindContentSections_GivesTheSectionsMediaPreparationBinds()
    {
        FoundryConfigurationDocument configuration = await ConfigurationAsync();
        using WinPeContentPackageMediaLease postInstallation = await new WinPePreOobeMediaService().PrepareAsync(library!, configuration.PreOobe, Cancellation);
        var generator = new DeployConfigurationGenerator();
        string media = WinPePreOobeMediaService.BindConfiguration(postInstallation, generator.Serialize(generator.Generate(configuration, null, null, null, null)));

        NetworkCacheContentSections sections = NetworkCacheBootConfiguration.BindContentSections(configuration, null, postInstallation, null);

        Assert.Equal(postInstallation.ManifestId, sections.PreOobe.ManifestId);
        Assert.Equal(JsonSerializer.Serialize(NetworkCacheBootConfiguration.Split(media).Sections), JsonSerializer.Serialize(sections));
    }

    private ContentPackageLibraryService? library;

    private async Task<FoundryConfigurationDocument> ConfigurationAsync()
    {
        string source = Path.Combine(root, "source");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "setup.ps1"), "exit 0", Cancellation);
        library = new ContentPackageLibraryService(Path.Combine(root, "library"), ContentPackageKind.PostInstallation);
        var reference = await library.ImportAsync(source, Cancellation);
        var action = PreOobeActionSettings.Create(PreOobeActionKind.PowerShell, "Setup") with { Package = reference, EntryPoint = "setup.ps1" };
        return new FoundryConfigurationDocument { PreOobe = new() { IsEnabled = true, Actions = [action] } };
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
