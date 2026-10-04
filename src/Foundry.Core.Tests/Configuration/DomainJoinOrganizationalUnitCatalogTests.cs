// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;

namespace Foundry.Core.Tests.Configuration;

public sealed class DomainJoinOrganizationalUnitCatalogTests
{
    [Fact]
    public void CatalogMergePreservesManualRowsAndDefault()
    {
        DomainJoinSettings current = Catalog();
        DomainJoinSettings merged = DomainJoinOrganizationalUnitCatalog.Merge(current, "CONTOSO.TEST.",
            [Unit("import", "Renamed", "ou=devices,dc=contoso,dc=test"), Unit("new", "Servers", "OU=Servers,DC=contoso,DC=test")]);
        Assert.Equal("manual", merged.DefaultOuId);
        Assert.Equal(2, merged.OrganizationalUnits.Count);
        Assert.Equal(current.OrganizationalUnits[0], merged.OrganizationalUnits[0]);
        Assert.Single(current.OrganizationalUnits);
    }

    [Fact]
    public void DomainChangeInvalidatesCatalogSelection()
    {
        DomainJoinSettings changed = Catalog() with { DomainName = "other.test" };
        Assert.Throws<ArgumentException>(() => DomainJoinOrganizationalUnitCatalog.Merge(changed, "other.test", []));
        Assert.False(DomainJoinConfigurationValidator.ValidateMetadata(changed).IsValid);
    }

    [Fact]
    public void ForeignDomainImportLeavesConfigurationUnchanged()
    {
        DomainJoinSettings current = Catalog();
        Assert.Throws<ArgumentException>(() => DomainJoinOrganizationalUnitCatalog.Merge(current, "other.test",
            [Unit("foreign", "Foreign", "OU=Foreign,DC=other,DC=test")]));
        Assert.Equal("contoso.test", current.DomainName);
        Assert.Equal("manual", current.DefaultOuId);
        Assert.Single(current.OrganizationalUnits);
    }

    [Fact]
    public void RemovingDefaultClearsDefault()
    {
        DomainJoinSettings removed = DomainJoinOrganizationalUnitCatalog.Remove(Catalog(), "MANUAL");
        Assert.Empty(removed.OrganizationalUnits);
        Assert.Null(removed.DefaultOuId);
        Assert.False(removed.AllowOuSelectionDuringDeployment);
        Assert.Null(removed.OuCatalogDomain);
        Assert.True(DomainJoinConfigurationValidator.ValidateMetadata(removed).IsValid);
    }

    [Fact]
    public void ImportRejectsDuplicateIdentityForDifferentDestination()
    {
        DomainJoinSettings current = Catalog();
        Assert.Throws<ArgumentException>(() => DomainJoinOrganizationalUnitCatalog.Merge(current, "contoso.test",
            [Unit("manual", "Servers", "OU=Servers,DC=contoso,DC=test")]));
        Assert.Single(current.OrganizationalUnits);
    }

    [Fact]
    public void RemovingRowsCanRepairForeignDomainDraft()
    {
        DomainJoinSettings draft = Catalog() with
        {
            DomainName = "other.test",
            OrganizationalUnits = [Unit("manual", "Devices", "OU=Devices,DC=contoso,DC=test"),
                Unit("server", "Servers", "OU=Servers,DC=contoso,DC=test")]
        };
        DomainJoinSettings remaining = DomainJoinOrganizationalUnitCatalog.Remove(draft, "manual");
        Assert.Single(remaining.OrganizationalUnits);
        Assert.Null(remaining.DefaultOuId);
        DomainJoinSettings repaired = DomainJoinOrganizationalUnitCatalog.Remove(remaining, "server");
        Assert.Empty(repaired.OrganizationalUnits);
        Assert.True(DomainJoinConfigurationValidator.ValidateMetadata(repaired).IsValid);
    }

    [Fact]
    public void SelectedImportEstablishesEmptyDomainAssociation()
    {
        DomainJoinSettings merged = DomainJoinOrganizationalUnitCatalog.Merge(new(), "CONTOSO.TEST.",
            [Unit("first", "Devices", "OU=Devices,DC=contoso,DC=test")]);
        Assert.Equal("contoso.test", merged.DomainName);
        Assert.Equal("contoso.test", merged.OuCatalogDomain);
    }

    [Fact]
    public void MergeRejectsOverflowAndOutsideDomain()
    {
        Assert.Throws<ArgumentException>(() => DomainJoinOrganizationalUnitCatalog.Merge(new(), "contoso.test",
            [Unit("foreign", "Foreign", "OU=Foreign,DC=other,DC=test")]));
        var units = Enumerable.Range(0, 1025).Select(i => Unit(i.ToString(), "OU", $"OU=Unit{i},DC=contoso,DC=test")).ToArray();
        Assert.Throws<ArgumentException>(() => DomainJoinOrganizationalUnitCatalog.Merge(new(), "contoso.test", units));
    }

    private static DomainJoinSettings Catalog() => new()
    {
        DomainName = "contoso.test",
        OuCatalogDomain = "contoso.test",
        DefaultOuId = "manual",
        AllowOuSelectionDuringDeployment = true,
        OrganizationalUnits = [Unit("manual", "Manual label", "OU=Devices,DC=contoso,DC=test")]
    };

    private static DomainJoinOrganizationalUnitSettings Unit(string id, string label, string dn) => new()
    { Id = id, DisplayName = label, DistinguishedName = dn };
}
