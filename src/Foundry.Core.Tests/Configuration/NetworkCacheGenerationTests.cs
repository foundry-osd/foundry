// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Core.Services.Autopilot;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.NetworkCache;

namespace Foundry.Core.Tests.Configuration;

public sealed class NetworkCacheGenerationTests
{
    private static readonly byte[] Key = new byte[32];

    [Fact]
    public void Generate_ShareWithAccount_WritesEnvelopeBoundToShareAndAccount()
    {
        DeployNetworkCacheSettings media = Generate(Settings(), new Dictionary<string, string> { ["a1"] = "share password" });

        DeployNetworkCacheShare share = Assert.Single(media.Shares, item => item.Id == "s1");
        Assert.Equal(@"\\fs01\cache", share.Path);
        Assert.Equal("CONTOSO\\svc", share.AccountName);
        byte[] plaintext = MediaSecretEnvelopeProtector.DecryptBytes(share.EncryptedCredentials!, Key, MediaSecretEnvelopeProtector.DeploymentKeyId);
        try
        {
            using NetworkShareCredentialPayload payload = NetworkShareCredentialPayloadCodec.Decode(plaintext, new(@"\\fs01\cache", "CONTOSO\\svc"));
            Assert.Equal("share password", new string(payload.Password.Span));
            Assert.Throws<CryptographicException>(() => NetworkShareCredentialPayloadCodec.Decode(plaintext, new(@"\\fs02\cache", "CONTOSO\\svc")));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    [Fact]
    public void Generate_ShareWithoutAccount_HasNoCredentials()
    {
        DeployNetworkCacheSettings media = Generate(Settings(), new Dictionary<string, string> { ["a1"] = "share password" });

        DeployNetworkCacheShare share = Assert.Single(media.Shares, item => item.Id == "s2");
        Assert.Null(share.AccountName);
        Assert.Null(share.EncryptedCredentials);
    }

    [Fact]
    public void Generate_CopiesScopesInOrder()
    {
        DeployNetworkCacheSettings media = Generate(Settings(), new Dictionary<string, string> { ["a1"] = "share password" });

        Assert.True(media.IsEnabled);
        DeployNetworkCacheScope scope = Assert.Single(media.Scopes);
        Assert.Equal(NetworkCacheScopeKind.Subnet, scope.Kind);
        Assert.Equal("10.0.0.0/8", scope.Value);
        Assert.Equal(["s2", "s1"], scope.ShareIds);
    }

    [Fact]
    public void Generate_Disabled_WritesDefaultSection()
    {
        DeployNetworkCacheSettings media = Generate(Settings() with { IsEnabled = false }, new Dictionary<string, string>());

        Assert.False(media.IsEnabled);
        Assert.Empty(media.Shares);
        Assert.Empty(media.Scopes);
    }

    [Fact]
    public void Generate_NotReady_Throws()
    {
        var generator = new DeployConfigurationGenerator();

        Assert.Throws<InvalidOperationException>(() => generator.Generate(
            new FoundryConfigurationDocument { NetworkCache = Settings() }, Key, new() { IsEnabled = true }, null, null,
            new MediaInputs { NetworkCachePasswords = new Dictionary<string, string>() }));
    }

    [Fact]
    public void Generate_AccountWithoutProtection_Throws()
    {
        var generator = new DeployConfigurationGenerator();

        Assert.Throws<InvalidOperationException>(() => generator.Generate(
            new FoundryConfigurationDocument { NetworkCache = Settings() }, null, null, null, null,
            new MediaInputs { NetworkCachePasswords = new Dictionary<string, string> { ["a1"] = "share password" } }));
    }

    private static DeployNetworkCacheSettings Generate(NetworkCacheSettings settings, IReadOnlyDictionary<string, string> passwords) =>
        new DeployConfigurationGenerator().Generate(
            new FoundryConfigurationDocument { NetworkCache = settings }, Key, new() { IsEnabled = true }, null, null,
            new MediaInputs { NetworkCachePasswords = passwords }).NetworkCache;

    private static NetworkCacheSettings Settings() => new()
    {
        IsEnabled = true,
        Accounts = [new() { Id = "a1", UserName = "CONTOSO\\svc" }],
        Shares = [new() { Id = "s1", Path = @"\\fs01\cache", AccountId = "a1" }, new() { Id = "s2", Path = @"\\fs02\cache" }],
        Scopes = [new() { Id = "c1", Name = "Paris", Kind = NetworkCacheScopeKind.Subnet, Value = "10.0.0.0/8", ShareIds = ["s2", "s1"] }]
    };
}
