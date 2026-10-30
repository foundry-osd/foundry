// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Configuration;

namespace Foundry.Connect.Tests;

public sealed class ProvisionedWifiProfileResolverTests
{
    [Fact]
    public void ResolveAssetPath_WhenPathIsRelative_UsesConfigurationDirectory()
    {
        using var tempDirectory = new TemporaryDirectory();
        string configurationPath = Path.Combine(tempDirectory.Path, "config", "foundry.connect.config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configurationPath)!);
        File.WriteAllText(configurationPath, "{}");

        string? resolved = ProvisionedWifiProfileResolver.ResolveAssetPath(
            @"Network\Wifi\Profiles\corp.xml",
            configurationPath);

        Assert.Equal(
            Path.GetFullPath(Path.Combine(tempDirectory.Path, "config", @"Network\Wifi\Profiles\corp.xml")),
            resolved);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Foundry.Connect.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
