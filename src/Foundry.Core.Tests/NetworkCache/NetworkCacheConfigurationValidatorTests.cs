// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.NetworkCache;

namespace Foundry.Core.Tests.NetworkCache;

public sealed class NetworkCacheConfigurationValidatorTests
{
    [Fact]
    public void Disabled_IsReady()
    {
        NetworkCacheValidationResult result = Evaluate(new NetworkCacheSettings { IsEnabled = false, Scopes = [Scope("c1", "bad", "missing")] });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidSettings_AreReady()
    {
        Assert.True(Evaluate(Valid()).IsValid);
    }

    [Fact]
    public void EnabledWithoutSharesOrScopes_ReportsBoth()
    {
        NetworkCacheValidationResult result = Evaluate(new NetworkCacheSettings { IsEnabled = true });

        Assert.Contains(result.Issues, issue => issue.Code == NetworkCacheValidationCode.SharesRequired);
        Assert.Contains(result.Issues, issue => issue.Code == NetworkCacheValidationCode.ScopesRequired);
    }

    [Theory]
    [InlineData("fs01\\cache")]
    [InlineData("\\\\fs01")]
    [InlineData("C:\\cache")]
    [InlineData("\\\\fs01\\cache\\..\\other")]
    [InlineData("")]
    public void InvalidSharePath_IsReported(string path)
    {
        NetworkCacheSettings settings = Valid() with { Shares = [new() { Id = "s1", Path = path, AccountId = "a1" }] };

        NetworkCacheValidationIssue issue = Assert.Single(Evaluate(settings).Issues);

        Assert.Equal(NetworkCacheValidationCode.InvalidSharePath, issue.Code);
        Assert.Equal("s1", issue.ItemId);
    }

    [Theory]
    [InlineData("\\\\fs01\\cache")]
    [InlineData("\\\\fs01.contoso.com\\cache$\\foundry")]
    [InlineData("\\\\10.0.0.5\\cache")]
    public void ValidSharePath_IsAccepted(string path)
    {
        Assert.True(NetworkCacheConfigurationValidator.IsValidSharePath(path));
    }

    [Fact]
    public void InvalidScopeValue_IsReported()
    {
        NetworkCacheSettings settings = Valid() with { Scopes = [Scope("c1", "10.0.0.0/33", "s1")] };

        NetworkCacheValidationIssue issue = Assert.Single(Evaluate(settings).Issues);

        Assert.Equal(NetworkCacheValidationCode.InvalidScope, issue.Code);
        Assert.Equal("c1", issue.ItemId);
    }

    [Fact]
    public void ScopeWithoutShare_IsReported()
    {
        NetworkCacheSettings settings = Valid() with { Scopes = [Scope("c1", "10.0.0.0/8")] };

        Assert.Equal(NetworkCacheValidationCode.ScopeWithoutShare, Assert.Single(Evaluate(settings).Issues).Code);
    }

    [Fact]
    public void ScopeReferencingUnknownShare_IsReported()
    {
        NetworkCacheSettings settings = Valid() with { Scopes = [Scope("c1", "10.0.0.0/8", "s1", "s9")] };

        Assert.Equal(NetworkCacheValidationCode.UnknownShare, Assert.Single(Evaluate(settings).Issues).Code);
    }

    [Fact]
    public void ShareReferencingUnknownAccount_IsReported()
    {
        NetworkCacheSettings settings = Valid() with { Shares = [new() { Id = "s1", Path = @"\\fs01\cache", AccountId = "a9" }] };

        Assert.Equal(NetworkCacheValidationCode.UnknownAccount, Assert.Single(Evaluate(settings).Issues).Code);
    }

    [Fact]
    public void AccountWithoutUserName_IsReported()
    {
        NetworkCacheSettings settings = Valid() with { Accounts = [new() { Id = "a1", UserName = " " }] };

        Assert.Contains(Evaluate(settings).Issues, issue => issue.Code == NetworkCacheValidationCode.UserNameRequired && issue.ItemId == "a1");
    }

    [Fact]
    public void UsedAccountWithoutPassword_IsReported()
    {
        NetworkCacheValidationResult result = NetworkCacheConfigurationValidator.EvaluateReadiness(Valid(), _ => false, isMediaProtected: true);

        NetworkCacheValidationIssue issue = Assert.Single(result.Issues);
        Assert.Equal(NetworkCacheValidationCode.PasswordRequired, issue.Code);
        Assert.Equal("a1", issue.ItemId);
    }

    [Fact]
    public void UnusedAccountWithoutPassword_IsNotReported()
    {
        NetworkCacheSettings settings = Valid() with
        {
            Accounts = [new() { Id = "a1", UserName = "CONTOSO\\svc" }, new() { Id = "a2", UserName = "CONTOSO\\spare" }]
        };

        Assert.True(NetworkCacheConfigurationValidator.EvaluateReadiness(settings, id => id == "a1", isMediaProtected: true).IsValid);
    }

    [Fact]
    public void ShareAccountWithoutProtection_RequiresProtection()
    {
        NetworkCacheValidationResult result = NetworkCacheConfigurationValidator.EvaluateReadiness(Valid(), _ => true, isMediaProtected: false);

        Assert.Equal(NetworkCacheValidationCode.MediaProtectionRequired, Assert.Single(result.Issues).Code);
    }

    [Fact]
    public void SharesWithoutAccounts_DoNotRequireProtection()
    {
        NetworkCacheSettings settings = Valid() with { Accounts = [], Shares = [new() { Id = "s1", Path = @"\\fs01\cache" }] };

        Assert.True(NetworkCacheConfigurationValidator.EvaluateReadiness(settings, _ => false, isMediaProtected: false).IsValid);
    }

    [Fact]
    public void RequiresMediaProtection_TrueOnlyWhenEnabledShareUsesAccount()
    {
        Assert.True(NetworkCacheConfigurationValidator.RequiresMediaProtection(Valid()));
        Assert.False(NetworkCacheConfigurationValidator.RequiresMediaProtection(Valid() with { IsEnabled = false }));
        Assert.False(NetworkCacheConfigurationValidator.RequiresMediaProtection(Valid() with { Shares = [new() { Id = "s1", Path = @"\\fs01\cache" }] }));
    }

    [Fact]
    public void Migration_DocumentWithoutSection_GetsDisabledDefault()
    {
        FoundryConfigurationDocument migrated = FoundryConfigurationMigration.ApplySchemaMigrations(new FoundryConfigurationDocument { NetworkCache = null! });

        Assert.NotNull(migrated.NetworkCache);
        Assert.False(migrated.NetworkCache.IsEnabled);
        Assert.Empty(migrated.NetworkCache.Shares);
    }

    private static NetworkCacheValidationResult Evaluate(NetworkCacheSettings settings) =>
        NetworkCacheConfigurationValidator.EvaluateReadiness(settings, _ => true, isMediaProtected: true);

    private static NetworkCacheSettings Valid() => new()
    {
        IsEnabled = true,
        Accounts = [new() { Id = "a1", UserName = "CONTOSO\\svc" }],
        Shares = [new() { Id = "s1", Path = @"\\fs01\cache", AccountId = "a1" }],
        Scopes = [Scope("c1", "10.0.0.0/8", "s1")]
    };

    private static NetworkCacheScope Scope(string id, string value, params string[] shareIds) => new()
    {
        Id = id,
        Name = id,
        Kind = NetworkCacheScopeKind.Subnet,
        Value = value,
        ShareIds = shareIds
    };
}
