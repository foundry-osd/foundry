// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.NetworkCache;

namespace Foundry.Core.Tests.NetworkCache;

public sealed class NetworkCacheBootConfigurationTests
{
    [Fact]
    public void Split_ActiveBoundConfiguration_EmptiesSectionsInBootJsonAndReturnsBoundSections()
    {
        FoundryDeployConfigurationDocument bound = Bound(publisherPublicKey: "a2V5");

        (string bootJson, NetworkCacheContentSections sections) = NetworkCacheBootConfiguration.Split(Serialize(bound));

        FoundryDeployConfigurationDocument boot = Deserialize(bootJson);
        Assert.Equal(Serialize(new DeployCustomImagesSettings()), Serialize(boot.CustomImages));
        Assert.Equal(Serialize(new DeployCustomDriversSettings()), Serialize(boot.CustomDrivers));
        Assert.Equal(Serialize(new DeployPreOobeSettings()), Serialize(boot.PreOobe));
        Assert.True(boot.NetworkCache.IsEnabled);
        Assert.Equal("a2V5", boot.NetworkCache.PublisherPublicKey);
        Assert.Equal("img-manifest", sections.CustomImages.ManifestId);
        Assert.Equal("img-hash", sections.CustomImages.ManifestHash);
        Assert.Equal("image-1", sections.CustomImages.DefaultImageId);
        DeployCustomDriverSet set = Assert.Single(sections.CustomDrivers.Sets);
        Assert.Equal("set-1", set.Id);
        Assert.Equal("drv-manifest", sections.CustomDrivers.ManifestId);
        Assert.Equal("set-1", sections.CustomDrivers.DefaultSetId);
        PreOobeActionSettings action = Assert.Single(sections.PreOobe.Actions);
        Assert.Equal("action-1", action.Id);
        Assert.Equal("oobe-manifest", sections.PreOobe.ManifestId);
        Assert.Equal("oobe-hash", sections.PreOobe.ManifestHash);
    }

    [Fact]
    public void Split_InactiveConfiguration_ReturnsTheSameJson()
    {
        string json = Serialize(Bound(publisherPublicKey: null) with { NetworkCache = new() });

        (string bootJson, NetworkCacheContentSections sections) = NetworkCacheBootConfiguration.Split(json);

        Assert.Equal(json, bootJson);
        Assert.Equal("img-manifest", sections.CustomImages.ManifestId);
    }

    [Fact]
    public void Split_EnabledCacheWithoutPublisherKey_ReturnsTheSameJson()
    {
        string json = Serialize(Bound(publisherPublicKey: null));

        (string bootJson, _) = NetworkCacheBootConfiguration.Split(json);

        Assert.Equal(json, bootJson);
    }

    private static FoundryDeployConfigurationDocument Bound(string? publisherPublicKey) => new()
    {
        NetworkCache = new() { IsEnabled = true, PublisherPublicKey = publisherPublicKey, PublisherKeyId = "0123", MinimumIndexSequence = 4 },
        CustomImages = new()
        {
            IsEnabled = true,
            DefaultSource = CustomImageSource.Custom,
            DefaultImageId = "image-1",
            DefaultImageIndex = 2,
            ManifestId = "img-manifest",
            ManifestHash = "img-hash"
        },
        CustomDrivers = new()
        {
            IsEnabled = true,
            Sets = [new() { Id = "set-1", DisplayName = "Set", ContentHash = "h", Length = 10, FileCount = 1 }],
            DefaultSetId = "set-1",
            ManifestId = "drv-manifest",
            ManifestHash = "drv-hash"
        },
        PreOobe = new()
        {
            IsEnabled = true,
            Actions = [new() { Id = "action-1", Name = "Action", Kind = PreOobeActionKind.Command, EntryPoint = "cmd.exe" }],
            ManifestId = "oobe-manifest",
            ManifestHash = "oobe-hash"
        }
    };

    private static string Serialize<T>(T document) =>
        JsonSerializer.Serialize(document, ConfigurationJsonDefaults.SerializerOptions);

    private static FoundryDeployConfigurationDocument Deserialize(string json) =>
        JsonSerializer.Deserialize<FoundryDeployConfigurationDocument>(json, ConfigurationJsonDefaults.SerializerOptions)!;
}
