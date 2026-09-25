// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text.Json;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Core.Models.Images;
using Foundry.Deploy.Services.Images;
using Foundry.Utilities.Storage;

namespace Foundry.Deploy.Tests;

public sealed class CustomImageCatalogTests
{
    [Fact]
    public async Task DiscoverAsync_OffersOnlyTopLevelManualWimsOnMatchingMedia()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            string directory = Path.Combine(root, "Foundry", "Images", "Custom");
            Directory.CreateDirectory(Path.Combine(directory, "manifests"));
            Directory.CreateDirectory(Path.Combine(directory, "nested"));
            await File.WriteAllTextAsync(Path.Combine(directory, "manual.wim"), "wim", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(directory, "nested", "hidden.wim"), "wim", TestContext.Current.CancellationToken);
            byte[] manifest = JsonSerializer.SerializeToUtf8Bytes(new CustomImageMediaManifest { ManifestId = "build" });
            await File.WriteAllBytesAsync(Path.Combine(directory, "manifests", "build.json"), manifest, TestContext.Current.CancellationToken);
            var settings = new DeployCustomImagesSettings { IsEnabled = true, ManifestId = "build", ManifestHash = Convert.ToHexString(SHA256.HashData(manifest)) };
            var service = new CustomImageCatalogService(new Volumes(root));
            var result = await service.DiscoverAsync(settings, TestContext.Current.CancellationToken);
            Assert.Single(result.Images);
            Assert.Equal("manual", result.Images[0].DisplayName);
            Assert.False(result.Images[0].IsManaged);
            var corrupt = await service.DiscoverAsync(settings with { ManifestHash = new string('0', 64) }, TestContext.Current.CancellationToken);
            Assert.Empty(corrupt.Images);
            Assert.NotEmpty(corrupt.ErrorKey);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class Volumes(string root) : IVolumeDiscovery
    {
        public IReadOnlyList<VolumeInfo> GetVolumes() => [new(root, "Foundry Cache", DriveType.Fixed, true, long.MaxValue)];
    }
}
