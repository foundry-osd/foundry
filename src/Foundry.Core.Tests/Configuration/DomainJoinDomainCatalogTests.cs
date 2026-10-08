// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;

namespace Foundry.Core.Tests.Configuration;

public sealed class DomainJoinDomainCatalogTests
{
    [Fact]
    public void FirstDomainBecomesTheDefault()
    {
        DomainJoinSettings added = DomainJoinDomainCatalog.Add(new(), " corp.test ", " CORP\\join ");
        DomainJoinDomainSettings domain = Assert.Single(added.Domains);
        Assert.Equal(domain.Id, added.DefaultDomainId);
        Assert.Equal("corp.test", domain.DomainName);
        Assert.Equal("CORP\\join", domain.AccountName);
        DomainJoinSettings second = DomainJoinDomainCatalog.Add(added, "emea.test", "  ");
        Assert.Equal(domain.Id, second.DefaultDomainId);
        Assert.Null(second.Domains[1].AccountName);
    }

    [Theory]
    [InlineData("CORP.TEST")]
    [InlineData("corp.test.")]
    [InlineData("bad_domain.test")]
    [InlineData("")]
    public void AddingADuplicateOrInvalidDomainIsRefused(string name)
    {
        DomainJoinSettings current = DomainJoinDomainCatalog.Add(new(), "corp.test", null);
        Assert.Throws<ArgumentException>(() => DomainJoinDomainCatalog.Add(current, name, null));
    }

    [Fact]
    public void RemovingTheDefaultMovesItToTheFirstRemainingDomain()
    {
        DomainJoinSettings current = Two();
        DomainJoinSettings removed = DomainJoinDomainCatalog.Remove(current, current.Domains[0].Id);
        Assert.Equal(current.Domains[1].Id, removed.DefaultDomainId);
        Assert.Equal(current.DefaultDomainId, DomainJoinDomainCatalog.Remove(current, current.Domains[1].Id).DefaultDomainId);
    }

    [Fact]
    public void RemovingTheLastDomainClearsTheDefault()
    {
        DomainJoinSettings current = DomainJoinDomainCatalog.Add(new(), "corp.test", null);
        DomainJoinSettings removed = DomainJoinDomainCatalog.Remove(current, current.Domains[0].Id);
        Assert.Empty(removed.Domains);
        Assert.Null(removed.DefaultDomainId);
        Assert.True(DomainJoinConfigurationValidator.ValidateMetadata(removed).IsValid);
    }

    [Fact]
    public void RenamingADomainThatHasOusIsRefused()
    {
        DomainJoinSettings current = Two();
        string id = current.Domains[0].Id;
        current = DomainJoinOrganizationalUnitCatalog.Merge(current, id,
            [new() { Id = "one", DisplayName = "Sales", DistinguishedName = "OU=Sales,DC=corp,DC=test" }]);
        Assert.Throws<ArgumentException>(() => DomainJoinDomainCatalog.Update(current, id, "corp2.test", null));
        DomainJoinSettings updated = DomainJoinDomainCatalog.Update(current, id, "CORP.test", "CORP\\join");
        Assert.Equal("CORP\\join", updated.Domains[0].AccountName);
        Assert.Single(updated.Domains[0].OrganizationalUnits);
    }

    [Fact]
    public void RenamingADomainKeepsItsDedicatedAccount()
    {
        DomainJoinSettings current = DomainJoinDomainCatalog.Add(new(), "corp.test", "CORP\\join");

        DomainJoinSettings renamed = DomainJoinDomainCatalog.Rename(current, current.Domains[0].Id, " lab.test ");

        Assert.Equal("lab.test", renamed.Domains[0].DomainName);
        Assert.Equal("CORP\\join", renamed.Domains[0].AccountName);
        Assert.Throws<ArgumentException>(() => DomainJoinDomainCatalog.Rename(current, "missing", "lab.test"));
    }

    [Fact]
    public void ADomainWithoutOusCanBeRenamedButNotToAnotherListedName()
    {
        DomainJoinSettings current = Two();
        Assert.Equal("lab.test", DomainJoinDomainCatalog.Update(current, current.Domains[1].Id, "lab.test", null).Domains[1].DomainName);
        Assert.Throws<ArgumentException>(() => DomainJoinDomainCatalog.Update(current, current.Domains[1].Id, "corp.test", null));
    }

    [Fact]
    public void DefaultCanOnlyNameAListedDomain()
    {
        DomainJoinSettings current = Two();
        Assert.Equal(current.Domains[1].Id, DomainJoinDomainCatalog.SetDefault(current, current.Domains[1].Id).DefaultDomainId);
        Assert.Throws<ArgumentException>(() => DomainJoinDomainCatalog.SetDefault(current, "missing"));
    }

    private static DomainJoinSettings Two() =>
        DomainJoinDomainCatalog.Add(DomainJoinDomainCatalog.Add(new(), "corp.test", null), "emea.test", null);
}
