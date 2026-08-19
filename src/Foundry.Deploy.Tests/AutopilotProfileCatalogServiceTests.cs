// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text;
using Foundry.Deploy.Models;
using Foundry.Deploy.Services.Autopilot;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class AutopilotProfileCatalogServiceTests
{
    [Fact]
    public void LoadAvailableProfiles_WhenPlaintextProfileExists_PreservesLegacyDiscovery()
    {
        string root = Path.Combine(Path.GetTempPath(), $"foundry-profile-catalog-{Guid.NewGuid():N}");
        string profileRoot = Path.Combine(root, "Legacy");
        Directory.CreateDirectory(profileRoot);
        string path = Path.Combine(profileRoot, "AutopilotConfigurationFile.json");
        File.WriteAllText(path, "plaintext");
        var service = new AutopilotProfileCatalogService(
            new FakeContentService("""{"Comment_File":"Legacy devices"}"""),
            NullLogger<AutopilotProfileCatalogService>.Instance,
            root);

        AutopilotProfileCatalogItem profile = Assert.Single(service.LoadAvailableProfiles());

        Assert.False(profile.IsProtected);
        Assert.Equal("Legacy devices", profile.DisplayName);
        Assert.Equal(path, profile.ConfigurationFilePath);
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void LoadAvailableProfiles_WhenEncryptedProfileExists_UsesDecryptedDisplayName()
    {
        string root = Path.Combine(Path.GetTempPath(), $"foundry-profile-catalog-{Guid.NewGuid():N}");
        string profileRoot = Path.Combine(root, "Corporate");
        Directory.CreateDirectory(profileRoot);
        string path = Path.Combine(profileRoot, "AutopilotConfigurationFile.json.encrypted");
        File.WriteAllText(path, "encrypted");
        var service = new AutopilotProfileCatalogService(
            new FakeContentService("""{"Comment_File":"Corporate devices"}"""),
            NullLogger<AutopilotProfileCatalogService>.Instance,
            root);

        IReadOnlyList<AutopilotProfileCatalogItem> profiles = service.LoadAvailableProfiles();

        AutopilotProfileCatalogItem profile = Assert.Single(profiles);
        Assert.True(profile.IsProtected);
        Assert.Equal("Corporate devices", profile.DisplayName);
        Assert.Equal(path, profile.ConfigurationFilePath);
        Directory.Delete(root, recursive: true);
    }

    private sealed class FakeContentService(string json) : IAutopilotProfileContentService
    {
        public Task<byte[]> ReadAsync(AutopilotProfileCatalogItem profile, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Encoding.UTF8.GetBytes(json));
        }
    }
}
