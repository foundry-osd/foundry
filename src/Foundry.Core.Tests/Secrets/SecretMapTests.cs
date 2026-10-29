// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Secrets;
using Foundry.Core.Tests.TestUtilities;

namespace Foundry.Core.Tests.Secrets;

public sealed class SecretMapTests
{
    private const string OldHash = "1111111111111111111111111111111111111111111111111111111111111111";
    private const string NewHash = "2222222222222222222222222222222222222222222222222222222222222222";

    [Fact]
    public void ToEntries_CertificatePasswordKeyedByFileHash()
    {
        FoundryConfigurationDocument configuration = WithDot1xCertificate(OldHash);

        IReadOnlyDictionary<string, string> entries = SecretMap.ToEntries(configuration, new SecretSnapshot { Dot1xCertificatePassword = "pfx-password" });

        Assert.Equal(new Dictionary<string, string> { [SecretIds.Dot1xCertificate(OldHash)] = "pfx-password" }, entries);
    }

    [Fact]
    public void NetworkCacheAccountPassword_RoundTripsByAccountIdWhileTheAccountExists()
    {
        FoundryConfigurationDocument configuration = WithNetworkCache(isEnabled: true);
        var secrets = new SecretSnapshot { NetworkCacheAccounts = new Dictionary<string, string> { ["a1"] = "share-password", ["gone"] = "old" } };

        IReadOnlyDictionary<string, string> entries = SecretMap.ToEntries(configuration, secrets);

        Assert.Equal(new Dictionary<string, string> { [SecretIds.NetworkCacheAccount("a1")] = "share-password" }, entries);
        Assert.Equal("share-password", SecretMap.FromEntries(configuration, entries).NetworkCacheAccounts["a1"]);
    }

    [Fact]
    public void RequiredIds_NetworkCacheAccount_OnlyWhenEnabledAndUsedByAShare()
    {
        Assert.Contains(SecretIds.NetworkCacheAccount("a1"), SecretMap.RequiredIds(WithNetworkCache(isEnabled: true)));
        Assert.DoesNotContain(SecretIds.NetworkCacheAccount("a2"), SecretMap.RequiredIds(WithNetworkCache(isEnabled: true)));
        Assert.Empty(SecretMap.RequiredIds(WithNetworkCache(isEnabled: false)));
    }

    private static FoundryConfigurationDocument WithNetworkCache(bool isEnabled) => new()
    {
        NetworkCache = new NetworkCacheSettings
        {
            IsEnabled = isEnabled,
            Accounts = [new() { Id = "a1", UserName = "CONTOSO\\svc" }, new() { Id = "a2", UserName = "CONTOSO\\spare" }],
            Shares = [new() { Id = "s1", Path = @"\\fs01\cache", AccountId = "a1" }]
        }
    };

    [Fact]
    public void FromEntries_ChangedCertificate_DoesNotReturnOldPassword()
    {
        var entries = new Dictionary<string, string> { [SecretIds.Dot1xCertificate(OldHash)] = "pfx-password" };

        SecretSnapshot secrets = SecretMap.FromEntries(WithDot1xCertificate(NewHash), entries);

        Assert.Null(secrets.Dot1xCertificatePassword);
        Assert.Equal("pfx-password", SecretMap.FromEntries(WithDot1xCertificate(OldHash), entries).Dot1xCertificatePassword);
    }

    [Fact]
    public void FromEntries_DomainAccountCanonicalized()
    {
        var configuration = new FoundryConfigurationDocument
        {
            DomainJoin = new DomainJoinSettings
            {
                IsEnabled = true,
                Mode = DomainJoinMode.Automatic,
                SharedAccountName = "  corp\\Join-Svc ",
                Domains = [new DomainJoinDomainSettings { Id = "d1", DomainName = "corp.example.test" }]
            }
        };
        var entries = new Dictionary<string, string> { [SecretIds.DomainAccount("CORP\\JOIN-SVC")] = "join-password" };

        SecretSnapshot secrets = SecretMap.FromEntries(configuration, entries);

        Assert.Equal("join-password", Assert.Single(secrets.DomainAccounts, pair => pair.Key == "CORP\\JOIN-SVC").Value);
        Assert.Equal(entries, SecretMap.ToEntries(configuration, new SecretSnapshot
        {
            DomainAccounts = new Dictionary<string, string> { ["corp\\join-svc"] = "join-password" }
        }));
    }

    [Fact]
    public void RequiredIds_DisabledFeatures_AreNotRequired()
    {
        FoundryConfigurationDocument enabled = new()
        {
            General = new GeneralSettings { DeploymentProtection = new DeploymentProtectionSettings { IsEnabled = true } },
            Network = new NetworkSettings
            {
                Dot1x = new Dot1xSettings { IsEnabled = true, RequiresCertificate = true, ClientCertificate = Reference(OldHash) },
                WifiProvisioned = true,
                Wifi = new WifiSettings { IsEnabled = true, Ssid = "Office", SecurityType = "WPA2-Personal" }
            },
            Customization = new CustomizationSettings
            {
                Oobe = new OobeSettings
                {
                    IsEnabled = true,
                    EnableAdministratorAccount = true,
                    UseAdministratorPassword = true,
                    AdditionalAccounts = [new OobeAdditionalAccountSettings { Id = "a1", UserName = "tech", UsePassword = true }]
                }
            },
            DomainJoin = new DomainJoinSettings
            {
                IsEnabled = true,
                Mode = DomainJoinMode.Automatic,
                SharedAccountName = "corp\\join",
                Domains = [new DomainJoinDomainSettings { Id = "d1", DomainName = "corp.example.test" }]
            }
        };
        FoundryConfigurationDocument disabled = enabled with
        {
            General = new GeneralSettings(),
            Network = enabled.Network with { Dot1x = enabled.Network.Dot1x with { IsEnabled = false }, Wifi = enabled.Network.Wifi with { IsEnabled = false } },
            Customization = new CustomizationSettings { Oobe = enabled.Customization.Oobe with { IsEnabled = false } },
            DomainJoin = enabled.DomainJoin with { IsEnabled = false }
        };

        Assert.Equal(
            new[]
            {
                SecretIds.DeploymentProtection, SecretIds.WifiPassphrase, SecretIds.Dot1xCertificate(OldHash),
                SecretIds.OobeAdministrator, SecretIds.OobeAccount("a1"), SecretIds.DomainAccount("CORP\\JOIN")
            }.Order(StringComparer.Ordinal),
            SecretMap.RequiredIds(enabled).Order(StringComparer.Ordinal));
        Assert.Empty(SecretMap.RequiredIds(disabled));
    }

    [Fact]
    public void Merge_KeepsEntriesTheMapDoesNotOwnAndReplacesOwnedOnes()
    {
        var existing = new Dictionary<string, string>
        {
            [SecretIds.SigningKey] = "signing-key",
            [SecretIds.MasterKey] = "master-key",
            [SecretIds.PublicationPassword] = "publication-password",
            ["future/feature/a1"] = "future-password",
            [SecretIds.WifiPassphrase] = "old-passphrase",
            [SecretIds.Dot1xCertificate(OldHash)] = "old-certificate-password",
        };
        var owned = new Dictionary<string, string>
        {
            [SecretIds.WifiPassphrase] = "new-passphrase",
            [SecretIds.Dot1xCertificate(NewHash)] = "new-certificate-password",
            [SecretIds.SigningKey] = "must-not-overwrite",
        };

        IReadOnlyDictionary<string, string> merged = SecretMap.Merge(existing, owned);

        Assert.Equal(new Dictionary<string, string>
        {
            [SecretIds.SigningKey] = "signing-key",
            [SecretIds.MasterKey] = "master-key",
            [SecretIds.PublicationPassword] = "publication-password",
            ["future/feature/a1"] = "future-password",
            [SecretIds.WifiPassphrase] = "new-passphrase",
            [SecretIds.Dot1xCertificate(NewHash)] = "new-certificate-password",
        }, merged);
    }

    [Fact]
    public void ToEntries_DisabledFeature_KeepsPasswordWhileItsOwnerExists()
    {
        FoundryConfigurationDocument disabled = WithDot1xCertificate(OldHash) with
        {
            Network = WithDot1xCertificate(OldHash).Network with { Dot1x = WithDot1xCertificate(OldHash).Network.Dot1x with { IsEnabled = false } }
        };

        IReadOnlyDictionary<string, string> entries = SecretMap.ToEntries(disabled, new SecretSnapshot { Dot1xCertificatePassword = "pfx-password" });

        Assert.Equal("pfx-password", entries[SecretIds.Dot1xCertificate(OldHash)]);
        Assert.Empty(SecretMap.RequiredIds(disabled));
    }

    [Fact]
    public void ClientCertificatePassword_KeepsItsVaultId()
    {
        AuthoringFileReference pfx = TestFileReferences.Detached("client.pfx");
        var configuration = new FoundryConfigurationDocument
        {
            Network = new NetworkSettings
            {
                Dot1x = new Dot1xSettings { IsEnabled = true, RequiresCertificate = true, ClientCertificate = pfx, TrustedRootCertificates = [TestFileReferences.Detached("root.cer")] }
            }
        };

        IReadOnlyDictionary<string, string> entries = SecretMap.ToEntries(configuration, new SecretSnapshot { Dot1xCertificatePassword = "pfx-password" });

        Assert.Equal("pfx-password", entries[SecretIds.Dot1xCertificate(pfx.ContentHash)]);
        Assert.Equal("pfx-password", SecretMap.FromEntries(configuration, entries).Dot1xCertificatePassword);
        Assert.Contains(SecretIds.Dot1xCertificate(pfx.ContentHash), SecretMap.RequiredIds(configuration));
    }

    [Fact]
    public void TrustedRootOnly_OwnsNoPassword()
    {
        var configuration = new FoundryConfigurationDocument
        {
            Network = new NetworkSettings { Dot1x = new Dot1xSettings { IsEnabled = true, RequiresCertificate = true, TrustedRootCertificates = [TestFileReferences.Detached("root.cer")] } }
        };

        Assert.Empty(SecretMap.ToEntries(configuration, new SecretSnapshot { Dot1xCertificatePassword = "unused" }));
    }

    private static FoundryConfigurationDocument WithDot1xCertificate(string hash) => new()
    {
        Network = new NetworkSettings
        {
            Dot1x = new Dot1xSettings { IsEnabled = true, RequiresCertificate = true, ClientCertificate = Reference(hash) }
        }
    };

    private static AuthoringFileReference Reference(string hash) => new() { ContentHash = hash, FileName = "machine.pfx", Length = 10 };
}
