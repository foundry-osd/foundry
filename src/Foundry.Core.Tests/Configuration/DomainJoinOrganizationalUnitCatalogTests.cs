// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;

namespace Foundry.Core.Tests.Configuration;

public sealed class DomainJoinOrganizationalUnitCatalogTests
{
    [Fact]
    public void MergeAddsOusToOneDomainOnlyAndSkipsListedOnes()
    {
        DomainJoinSettings current = Catalog();
        DomainJoinSettings merged = DomainJoinOrganizationalUnitCatalog.Merge(current, "corp",
            [Unit("import", "Renamed", "ou=devices,dc=corp,dc=test"), Unit("new", "Servers", "OU=Servers,DC=corp,DC=test")]);
        Assert.Equal(2, merged.Domains[0].OrganizationalUnits.Count);
        Assert.Equal(current.Domains[0].OrganizationalUnits[0], merged.Domains[0].OrganizationalUnits[0]);
        Assert.Equal("manual", merged.Domains[0].DefaultOuId);
        Assert.Same(current.Domains[1], merged.Domains[1]);
        Assert.Single(current.Domains[0].OrganizationalUnits);
    }

    [Fact]
    public void MergeRefusesAnOuOfAnotherDomainOrAnUnknownDomain()
    {
        Assert.Throws<ArgumentException>(() => DomainJoinOrganizationalUnitCatalog.Merge(Catalog(), "corp",
            [Unit("x", "Other", "OU=Other,DC=emea,DC=test")]));
        Assert.Throws<ArgumentException>(() => DomainJoinOrganizationalUnitCatalog.Merge(Catalog(), "missing",
            [Unit("x", "Other", "OU=Other,DC=corp,DC=test")]));
        Assert.Throws<ArgumentException>(() => DomainJoinOrganizationalUnitCatalog.Merge(Catalog(), "corp",
            [Unit("x", "Container", "CN=Computers,DC=corp,DC=test")]));
    }

    [Fact]
    public void RemovingTheDefaultOuClearsThatDomainsDefault()
    {
        DomainJoinSettings removed = DomainJoinOrganizationalUnitCatalog.Remove(Catalog(), "corp", "manual");
        Assert.Empty(removed.Domains[0].OrganizationalUnits);
        Assert.Null(removed.Domains[0].DefaultOuId);
    }

    [Fact]
    public void DefaultOuCanBeSetClearedAndMustBelongToTheDomain()
    {
        DomainJoinSettings current = Catalog();
        Assert.Null(DomainJoinOrganizationalUnitCatalog.SetDefault(current, "corp", null).Domains[0].DefaultOuId);
        Assert.Equal("manual", DomainJoinOrganizationalUnitCatalog.SetDefault(current, "corp", "manual").Domains[0].DefaultOuId);
        Assert.Throws<ArgumentException>(() => DomainJoinOrganizationalUnitCatalog.SetDefault(current, "emea", "manual"));
    }

    [Fact]
    public void ExcludeListedKeepsOnlyOusAnImportWouldAdd()
    {
        DomainJoinOrganizationalUnitSettings added = Unit("new", "Servers", "OU=Servers,DC=corp,DC=test");

        IReadOnlyList<DomainJoinOrganizationalUnitSettings> remaining = DomainJoinOrganizationalUnitCatalog.ExcludeListed(Catalog().Domains[0],
            [Unit("import", "Renamed", "ou=devices,dc=corp,dc=test"), added]);

        Assert.Equal([added], remaining);
    }

    private static DomainJoinSettings Catalog() => new()
    {
        DefaultDomainId = "corp",
        Domains =
        [
            new() { Id = "corp", DomainName = "corp.test", DefaultOuId = "manual", OrganizationalUnits = [Unit("manual", "Devices", "OU=Devices,DC=corp,DC=test")] },
            new() { Id = "emea", DomainName = "emea.test" }
        ]
    };

    private static DomainJoinOrganizationalUnitSettings Unit(string id, string name, string distinguishedName) =>
        new() { Id = id, DisplayName = name, DistinguishedName = distinguishedName };
}
