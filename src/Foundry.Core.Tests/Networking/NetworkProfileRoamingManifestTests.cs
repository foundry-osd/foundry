// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Foundry.Core.Models.Network;

namespace Foundry.Core.Tests.Networking;

public sealed class NetworkProfileRoamingManifestTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public void RoundTrip_KeepsOrderedWifiProfilesAndFirstEntry()
    {
        NetworkProfileRoamingProfile first = Profile("wifi-profile.xml", NetworkProfileRoamingArtifacts.ProvisionedWifiSource);
        NetworkProfileRoamingProfile second = Profile("wifi-profile-2.xml", NetworkProfileRoamingArtifacts.ManualWifiSource);
        var manifest = new NetworkProfileRoamingManifest { WifiProfile = first, WifiProfiles = [first, second] };

        NetworkProfileRoamingManifest? read = JsonSerializer.Deserialize<NetworkProfileRoamingManifest>(
            JsonSerializer.Serialize(manifest, JsonOptions),
            JsonOptions);

        Assert.NotNull(read);
        Assert.Equal(NetworkProfileRoamingManifest.CurrentSchemaVersion, read.SchemaVersion);
        Assert.Equal(2, read.SchemaVersion);
        Assert.Equal(first, read.WifiProfile);
        Assert.Equal([first, second], read.GetOrderedWifiProfiles());
    }

    [Fact]
    public void Read_SchemaOneManifest_FallsBackToTheSingleWifiProfile()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "wifiProfile": {
                "relativePath": "wifi-profile.xml",
                "source": "manualWifi",
                "connectivityExpectation": "preOobeConnectable"
              }
            }
            """;

        NetworkProfileRoamingManifest? read = JsonSerializer.Deserialize<NetworkProfileRoamingManifest>(json, JsonOptions);

        Assert.NotNull(read);
        Assert.Equal(1, read.SchemaVersion);
        Assert.Empty(read.WifiProfiles);
        NetworkProfileRoamingProfile profile = Assert.Single(read.GetOrderedWifiProfiles());
        Assert.Equal("wifi-profile.xml", profile.RelativePath);
        Assert.Equal("manualWifi", profile.Source);
    }

    [Fact]
    public void GetOrderedWifiProfiles_WithoutWifi_IsEmpty()
    {
        Assert.Empty(new NetworkProfileRoamingManifest().GetOrderedWifiProfiles());
        Assert.Empty(new NetworkProfileRoamingManifest { WifiProfiles = null! }.GetOrderedWifiProfiles());
    }

    [Theory]
    [InlineData(0, "wifi-profile.xml")]
    [InlineData(1, "wifi-profile-2.xml")]
    [InlineData(2, "wifi-profile-3.xml")]
    public void GetWifiProfileFileName_NumbersProfilesAfterTheFirst(int index, string expected)
    {
        Assert.Equal(expected, NetworkProfileRoamingArtifacts.GetWifiProfileFileName(index));
    }

    [Fact]
    public void GetWifiProfileFileName_RejectsNegativeIndex()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NetworkProfileRoamingArtifacts.GetWifiProfileFileName(-1));
    }

    private static NetworkProfileRoamingProfile Profile(string relativePath, string source) => new()
    {
        RelativePath = relativePath,
        Source = source,
        ConnectivityExpectation = NetworkProfileRoamingArtifacts.PreOobeConnectableExpectation
    };
}
