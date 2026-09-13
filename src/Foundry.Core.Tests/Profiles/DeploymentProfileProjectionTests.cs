// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using Foundry.Core.Models.Profiles;
using Foundry.Core.Services.Profiles;

namespace Foundry.Core.Tests.Profiles;

public sealed class DeploymentProfileProjectionTests
{
    [Fact]
    public void PortableContent_IgnoresHostPathsTelemetryAndRecordOrdering()
    {
        DeploymentProfileDocument first = CreateProfile();
        DeploymentProfileDocument second = first with
        {
            Configuration = first.Configuration with
            {
                General = first.Configuration.General with { IsoOutputPath = "other.iso", CustomDriverDirectoryPath = "other-drivers" },
                Network = first.Configuration.Network with
                {
                    Dot1x = first.Configuration.Network.Dot1x with { CertificatePath = "other.pfx", ProfileTemplatePath = "other.xml" }
                },
                Telemetry = first.Configuration.Telemetry with { InstallId = "other-installation", IsEnabled = true }
            },
            Assets = first.Assets.Reverse().ToArray(),
            Secrets = new() { Entries = first.Secrets.Entries.Reverse().ToArray() }
        };

        Assert.True(DeploymentProfileProjection.HasSamePortableContent(first, second));
        Assert.Equal(new byte[] { 1, 2, 3 }, first.Assets[0].Content);
        Assert.Equal(new byte[] { 4, 5, 6 }, first.Secrets.Entries[0].Value);
    }

    [Theory]
    [InlineData("settings")]
    [InlineData("password")]
    [InlineData("file")]
    [InlineData("name")]
    [InlineData("identity")]
    public void PortableContent_DetectsChangedSettingsPasswordsAndFiles(string change)
    {
        DeploymentProfileDocument first = CreateProfile();
        DeploymentProfileDocument second = change switch
        {
            "settings" => first with
            {
                Configuration = first.Configuration with
                {
                    Network = first.Configuration.Network with { Wifi = first.Configuration.Network.Wifi with { Ssid = "another-network" } }
                }
            },
            "password" => first with { Secrets = new() { Entries = [first.Secrets.Entries[0] with { Value = [7, 8, 9] }, first.Secrets.Entries[1]] } },
            "file" => first with { Assets = [first.Assets[0] with { Content = [7, 8, 9], Sha256 = Convert.ToHexString(SHA256.HashData(new byte[] { 7, 8, 9 })) }, first.Assets[1]] },
            "name" => first with { DisplayName = "Renamed settings" },
            _ => first with { ProfileId = Guid.NewGuid() }
        };

        Assert.False(DeploymentProfileProjection.HasSamePortableContent(first, second));
    }

    private static DeploymentProfileDocument CreateProfile() => new()
    {
        ProfileId = Guid.NewGuid(),
        DisplayName = "Settings",
        Assets =
        [
            new() { Id = "wired", Kind = ProfileAssetKind.WiredCertificate, RelativePath = "assets/wired.pfx", State = ProfileValueState.Present,
                Content = [1, 2, 3], Sha256 = Convert.ToHexString(SHA256.HashData(new byte[] { 1, 2, 3 })) },
            new() { Id = "wifi", Kind = ProfileAssetKind.WifiCertificate, RelativePath = "assets/wifi.pfx", State = ProfileValueState.Present,
                Content = [1, 2, 3], Sha256 = Convert.ToHexString(SHA256.HashData(new byte[] { 1, 2, 3 })) }
        ],
        Secrets = new()
        {
            Entries =
        [
            new() { Purpose = ProfileSecretPurpose.WiredCertificatePassword, Identity = "wired", State = ProfileValueState.Present, Value = [4, 5, 6] },
            new() { Purpose = ProfileSecretPurpose.WifiCertificatePassword, Identity = "wifi", State = ProfileValueState.Present, Value = [4, 5, 6] }
        ]
        }
    };
}
