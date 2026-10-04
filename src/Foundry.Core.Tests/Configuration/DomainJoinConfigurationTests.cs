// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Foundry.Core.Models.PreOobe;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;

namespace Foundry.Core.Tests.Configuration;

public sealed class DomainJoinConfigurationTests
{
    [Fact]
    public void PreparedReceipt_RoundTripsExplicitUnassignedSeedAndPreservesExistingNumericKinds()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var seed = JsonSerializer.Deserialize<DomainJoinPhaseReceipt>(JsonSerializer.Serialize(new DomainJoinPhaseReceipt
        { OperationId = "a", AttemptId = "b", PlanHash = "c", ActionId = "join" }, options), options)!;
        Assert.Equal(DomainJoinReceiptPhase.Prepared, seed.Phase); Assert.Null(seed.OriginatingBootId);
        Assert.Equal(DomainJoinPhaseState.NotStarted, seed.Join.State); Assert.Equal(DomainJoinPhaseState.NotStarted, seed.Placement.State);
        Assert.Equal(PreOobeBuiltInKind.Cleanup, JsonSerializer.Deserialize<PreOobeBuiltInKind>("5"));
        Assert.Equal(PreOobeBuiltInKind.DomainJoinAndPlacement, JsonSerializer.Deserialize<PreOobeBuiltInKind>("6"));
        Assert.Equal(PreOobeBuiltInKind.VerifyDomainMembership, JsonSerializer.Deserialize<PreOobeBuiltInKind>("7"));
    }

    [Fact]
    public void PhaseResults_PreserveNumericErrorFamilyAndAcceptOmittedServerCode()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var legacy = JsonSerializer.Deserialize<DomainJoinPhaseResult>("""{"state":2,"failureCode":4,"ldapErrorCode":49}""", options)!;
        Assert.Equal(49, legacy.LdapErrorCode); Assert.Null(legacy.DirectoryResultCode);
        var server = legacy with { LdapErrorCode = null, DirectoryResultCode = 50 };
        var restored = JsonSerializer.Deserialize<DomainJoinPhaseResult>(JsonSerializer.Serialize(server, options), options)!;
        Assert.Equal(50, restored.DirectoryResultCode); Assert.Null(restored.LdapErrorCode); Assert.Null(restored.NativeErrorCode);
    }

    [Fact]
    public void OmittedPasswordIsValidButAutomaticNotReady()
    {
        DomainJoinSettings settings = ReadyAutomatic();
        Assert.True(DomainJoinConfigurationValidator.ValidateMetadata(settings).IsValid);
        DomainJoinValidationResult result = DomainJoinConfigurationValidator.EvaluateReadiness(settings, false, false);
        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == DomainJoinValidationCode.PasswordRequired);
        Assert.Contains(result.Issues, issue => issue.Code == DomainJoinValidationCode.MediaProtectionRequired);
    }

    [Fact]
    public void InteractiveNeedsNoMediaPassword()
    {
        var settings = new DomainJoinSettings { IsEnabled = true, Mode = DomainJoinMode.Interactive };
        Assert.True(DomainJoinConfigurationValidator.EvaluateReadiness(settings, false, false).IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("technician")]
    [InlineData("CONTOSO\\")]
    [InlineData("@contoso.test")]
    [InlineData("a@b@c")]
    public void IncompleteDraftAccountIsImportableButNotReady(string? account)
    {
        DomainJoinSettings settings = ReadyAutomatic() with { AccountName = account };
        Assert.True(DomainJoinConfigurationValidator.ValidateMetadata(settings).IsValid);
        Assert.False(DomainJoinConfigurationValidator.EvaluateReadiness(settings, true, true).IsValid);
    }

    [Theory]
    [InlineData("CONTOSO\\technician")]
    [InlineData("technician@contoso.test")]
    public void QualifiedAutomaticAccountIsReady(string account)
    {
        Assert.True(DomainJoinConfigurationValidator.EvaluateReadiness(ReadyAutomatic() with { AccountName = account }, true, true).IsValid);
    }

    [Fact]
    public void MissingDraftDomainIsImportableButAutomaticNotReady()
    {
        DomainJoinSettings settings = ReadyAutomatic() with { DomainName = null };
        Assert.True(DomainJoinConfigurationValidator.ValidateMetadata(settings).IsValid);
        Assert.False(DomainJoinConfigurationValidator.EvaluateReadiness(settings, true, true).IsValid);
    }

    [Fact]
    public void ContradictoryModesAreRejected()
    {
        Assert.Throws<InvalidOperationException>(() => DomainJoinConfigurationValidator.ThrowIfProvisioningModesConflict(
            new AutopilotSettings { IsEnabled = true }, ReadyAutomatic()));
    }

    [Fact]
    public void CatalogAllowsDuplicateLabelsButRejectsDuplicateIdentifiersAndNames()
    {
        DomainJoinSettings settings = Catalog();
        Assert.True(DomainJoinConfigurationValidator.ValidateMetadata(settings).IsValid);
        Assert.False(DomainJoinConfigurationValidator.ValidateMetadata(settings with
        {
            OrganizationalUnits = [settings.OrganizationalUnits[0], settings.OrganizationalUnits[1] with { Id = "one" }]
        }).IsValid);
        Assert.False(DomainJoinConfigurationValidator.ValidateMetadata(settings with
        {
            OrganizationalUnits = [settings.OrganizationalUnits[0], settings.OrganizationalUnits[1] with { DistinguishedName = "ou=Sales,dc=CONTOSO,dc=test" }]
        }).IsValid);
    }

    [Fact]
    public void CatalogRequiresMatchingDomainAndAvailableDefault()
    {
        DomainJoinSettings settings = Catalog();
        Assert.False(DomainJoinConfigurationValidator.ValidateMetadata(settings with { DefaultOuId = "deleted" }).IsValid);
        Assert.False(DomainJoinConfigurationValidator.ValidateMetadata(settings with { OuCatalogDomain = null }).IsValid);
        Assert.False(DomainJoinConfigurationValidator.ValidateMetadata(settings with { DomainName = "other.test" }).IsValid);
        Assert.False(DomainJoinConfigurationValidator.ValidateMetadata(settings with { OrganizationalUnits = [], DefaultOuId = null }).IsValid);
        Assert.False(DomainJoinConfigurationValidator.ValidateMetadata(settings with
        {
            OrganizationalUnits = [settings.OrganizationalUnits[0] with { DistinguishedName = "OU=Sales,DC=other,DC=test" }]
        }).IsValid);
    }

    [Fact]
    public void MetadataRejectsMaximumPlusOneBoundsAndEmbeddedNull()
    {
        DomainJoinSettings settings = Catalog();
        foreach ((DomainJoinSettings invalid, DomainJoinValidationCode code) in new[]
        {
            (settings with { DomainName = new string('a', 254) }, DomainJoinValidationCode.InvalidDomainName),
            (settings with { AccountName = new string('a', 513) }, DomainJoinValidationCode.InvalidAccountName),
            (settings with { AccountName = "domain\\a\0b" }, DomainJoinValidationCode.InvalidAccountName),
            (settings with { OrganizationalUnits = Enumerable.Range(0, 1025).Select(i => settings.OrganizationalUnits[0] with { Id = i.ToString(), DistinguishedName = $"OU=OU{i},DC=contoso,DC=test" }).ToArray() }, DomainJoinValidationCode.TooManyOrganizationalUnits),
            (settings with { OrganizationalUnits = [settings.OrganizationalUnits[0] with { Id = new string('a', 129) }] }, DomainJoinValidationCode.InvalidOuId),
            (settings with { OrganizationalUnits = [settings.OrganizationalUnits[0] with { DisplayName = new string('a', 121) }] }, DomainJoinValidationCode.InvalidOuDisplayName),
            (settings with { OrganizationalUnits = [settings.OrganizationalUnits[0] with { DistinguishedName = "OU=" + new string('a', 4097) }] }, DomainJoinValidationCode.InvalidDistinguishedName)
        })
        {
            DomainJoinValidationResult result = DomainJoinConfigurationValidator.ValidateMetadata(invalid);
            Assert.False(result.IsValid);
            Assert.Contains(result.Issues, issue => issue.Code == code);
        }
    }

    [Fact]
    public void MetadataAcceptsExactBoundsAndRetainsSourceDistinguishedName()
    {
        string id = new('i', 128);
        string suffix = ",DC=contoso,DC=test";
        string dn = "OU=" + new string('a', 4096 - 3 - suffix.Length) + suffix;
        DomainJoinSettings settings = Catalog() with
        {
            AccountName = new string('a', 512),
            DefaultOuId = id,
            OrganizationalUnits = [new() { Id = id, DisplayName = new string('a', 120), DistinguishedName = dn }]
        };
        Assert.True(DomainJoinConfigurationValidator.ValidateMetadata(settings).IsValid);
        Assert.Equal(dn, settings.OrganizationalUnits[0].DistinguishedName);
        settings = Catalog() with
        {
            DefaultOuId = "0",
            OrganizationalUnits = Enumerable.Range(0, 1024).Select(i => new DomainJoinOrganizationalUnitSettings
            {
                Id = i.ToString(),
                DisplayName = "Computers",
                DistinguishedName = $"OU=OU{i},DC=contoso,DC=test"
            }).ToArray()
        };
        Assert.True(DomainJoinConfigurationValidator.ValidateMetadata(settings).IsValid);
        string maximumDomain = string.Join('.', new string('a', 63), new string('b', 63), new string('c', 63), new string('d', 61));
        Assert.True(DomainJoinConfigurationValidator.EvaluateReadiness(ReadyAutomatic() with { DomainName = maximumDomain }, true, true).IsValid);
    }

    [Theory]
    [InlineData("partial.")]
    [InlineData("bad_domain.test")]
    [InlineData("-contoso.test")]
    public void IncompleteDraftDomainIsImportableButNotReady(string domain)
    {
        DomainJoinSettings settings = ReadyAutomatic() with { DomainName = domain };
        Assert.True(DomainJoinConfigurationValidator.ValidateMetadata(settings).IsValid);
        Assert.False(DomainJoinConfigurationValidator.EvaluateReadiness(settings, true, true).IsValid);
        DomainJoinSettings catalogDraft = Catalog() with { DomainName = domain };
        Assert.True(DomainJoinConfigurationValidator.ValidateMetadata(catalogDraft).IsValid);
        Assert.False(DomainJoinConfigurationValidator.EvaluateReadiness(catalogDraft, true, true).IsValid);
    }

    [Fact]
    public void CredentialOwnershipCanonicalizesDomainAndAccountButRejectsChangedOwners()
    {
        var context = new DomainJoinCredentialContext("Contoso.TEST.", "contoso\\Technician");
        Assert.True(context.Matches(new("contoso.test", "CONTOSO\\technician")));
        Assert.False(context.Matches(new("other.test", "CONTOSO\\technician")));
        Assert.False(context.Matches(new("contoso.test", "CONTOSO\\other")));
    }

    private static DomainJoinSettings ReadyAutomatic() => new()
    {
        IsEnabled = true,
        Mode = DomainJoinMode.Automatic,
        DomainName = "contoso.test",
        AccountName = "CONTOSO\\technician"
    };

    private static DomainJoinSettings Catalog() => ReadyAutomatic() with
    {
        OuCatalogDomain = "contoso.test",
        AllowOuSelectionDuringDeployment = true,
        DefaultOuId = "one",
        OrganizationalUnits =
        [
            new() { Id = "one", DisplayName = "Computers", DistinguishedName = "OU=Sales,DC=contoso,DC=test" },
            new() { Id = "two", DisplayName = "Computers", DistinguishedName = "OU=Engineering,DC=contoso,DC=test" }
        ]
    };
}
