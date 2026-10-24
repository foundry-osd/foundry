// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Secrets;
using Foundry.Core.Services.Sharing;

namespace Foundry.Core.Tests.Sharing;

public sealed class PublicationFingerprintTests
{
    private const string Folder = "C:\\Share\\Foundry";

    private static readonly FoundryConfigurationDocument Configuration = new()
    {
        General = new GeneralSettings { IsoOutputPath = "C:\\Media\\foundry.iso", WinPeLanguage = "en-US" },
        Network = new NetworkSettings
        {
            WifiProvisioned = true,
            Wifi = new WifiSettings { IsEnabled = true, Ssid = "Office", SecurityType = "WPA2-Personal" }
        }
    };

    private static readonly Dictionary<string, string> Vault = new(StringComparer.Ordinal)
    {
        [SecretIds.WifiPassphrase] = "first-passphrase",
        [SecretIds.SigningKey] = "signing-key"
    };

    [Fact]
    public void Compute_ChangesOnlyWithWhatIsPublished()
    {
        string published = PublicationFingerprint.Compute(Folder, includePasswords: true, Configuration, Vault);
        Dictionary<string, string> otherPassphrase = new(Vault) { [SecretIds.WifiPassphrase] = "second-passphrase" };
        Dictionary<string, string> otherSharingKey = new(Vault) { [SecretIds.SigningKey] = "other-signing-key" };
        FoundryConfigurationDocument perPcChange = Configuration with { General = Configuration.General with { IsoOutputPath = "D:\\foundry.iso" } };
        FoundryConfigurationDocument sharedChange = Configuration with { General = Configuration.General with { WinPeLanguage = "fr-FR" } };

        Assert.Equal(published, PublicationFingerprint.Compute(Folder, includePasswords: true, perPcChange, Vault));
        Assert.Equal(published, PublicationFingerprint.Compute(Folder, includePasswords: true, Configuration, otherSharingKey));
        Assert.NotEqual(published, PublicationFingerprint.Compute(Folder, includePasswords: true, sharedChange, Vault));
        Assert.NotEqual(published, PublicationFingerprint.Compute(Folder, includePasswords: true, Configuration, otherPassphrase));
        Assert.NotEqual(published, PublicationFingerprint.Compute("D:\\Other", includePasswords: true, Configuration, Vault));
        Assert.Equal(
            PublicationFingerprint.Compute(Folder, includePasswords: false, Configuration, Vault),
            PublicationFingerprint.Compute(Folder, includePasswords: false, Configuration, otherPassphrase));
    }
}
