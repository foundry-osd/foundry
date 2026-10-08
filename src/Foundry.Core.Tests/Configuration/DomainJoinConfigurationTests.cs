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
    public void MetadataAcceptsAnEmptyDomainList()
    {
        Assert.True(DomainJoinConfigurationValidator.ValidateMetadata(new()).IsValid);
    }

    [Theory]
    [InlineData("corp.test", "CORP.TEST")]
    [InlineData("corp.test", "corp.test.")]
    public void DomainsThatDifferOnlyByCaseOrTrailingDotAreDuplicates(string first, string second)
    {
        DomainJoinSettings settings = With(Domain("a", first), Domain("b", second));
        Assert.Contains(DomainJoinConfigurationValidator.ValidateMetadata(settings).Issues,
            issue => issue.Code == DomainJoinValidationCode.DuplicateDomainName && issue.DomainId == "b");
    }

    [Fact]
    public void DomainIdsMustBeUnique()
    {
        DomainJoinSettings settings = With(Domain("a", "corp.test"), Domain("A", "emea.test"));
        Assert.Contains(DomainJoinConfigurationValidator.ValidateMetadata(settings).Issues, issue => issue.Code == DomainJoinValidationCode.DuplicateDomainId);
    }

    [Fact]
    public void DefaultDomainMustBeListedWhenDomainsExist()
    {
        DomainJoinSettings settings = With(Domain("a", "corp.test")) with { DefaultDomainId = "missing" };
        Assert.Contains(DomainJoinConfigurationValidator.ValidateMetadata(settings).Issues, issue => issue.Code == DomainJoinValidationCode.DefaultDomainMissing);
        Assert.Contains(DomainJoinConfigurationValidator.ValidateMetadata(settings with { DefaultDomainId = null }).Issues,
            issue => issue.Code == DomainJoinValidationCode.DefaultDomainMissing);
    }

    [Fact]
    public void MoreThanThirtyTwoDomainsAreRefused()
    {
        DomainJoinDomainSettings[] domains = Enumerable.Range(0, 33).Select(index => Domain($"d{index}", $"d{index}.test")).ToArray();
        Assert.True(DomainJoinConfigurationValidator.ValidateMetadata(With(domains.Take(32).ToArray())).IsValid);
        Assert.Contains(DomainJoinConfigurationValidator.ValidateMetadata(With(domains)).Issues, issue => issue.Code == DomainJoinValidationCode.TooManyDomains);
    }

    [Fact]
    public void AnOuMustBelongToItsOwnDomain()
    {
        DomainJoinSettings settings = With(Domain("a", "corp.test", Unit("one", "OU=Sales,DC=other,DC=test")));
        Assert.Contains(DomainJoinConfigurationValidator.ValidateMetadata(settings).Issues,
            issue => issue.Code == DomainJoinValidationCode.OuOutsideDomain && issue.DomainId == "a");
    }

    [Fact]
    public void DefaultOuMustBelongToTheSameDomain()
    {
        DomainJoinSettings settings = With(
            Domain("a", "corp.test", Unit("one", "OU=Sales,DC=corp,DC=test")) with { DefaultOuId = "two" },
            Domain("b", "emea.test", Unit("two", "OU=Sales,DC=emea,DC=test")));
        Assert.Contains(DomainJoinConfigurationValidator.ValidateMetadata(settings).Issues,
            issue => issue.Code == DomainJoinValidationCode.DefaultOuMissing && issue.DomainId == "a");
    }

    [Fact]
    public void TheSameOuIdMayExistInTwoDomainsButNotTwiceInOne()
    {
        Assert.True(DomainJoinConfigurationValidator.ValidateMetadata(With(
            Domain("a", "corp.test", Unit("one", "OU=Sales,DC=corp,DC=test")),
            Domain("b", "emea.test", Unit("one", "OU=Sales,DC=emea,DC=test")))).IsValid);
        DomainJoinValidationResult result = DomainJoinConfigurationValidator.ValidateMetadata(With(Domain("a", "corp.test",
            Unit("one", "OU=Sales,DC=corp,DC=test"), Unit("ONE", "OU=Field,DC=corp,DC=test"), Unit("two", "ou=sales,dc=corp,dc=test"))));
        Assert.Contains(result.Issues, issue => issue.Code == DomainJoinValidationCode.DuplicateOuId);
        Assert.Contains(result.Issues, issue => issue.Code == DomainJoinValidationCode.DuplicateDistinguishedName);
    }

    [Fact]
    public void ChoiceSettingsNeverInvalidateAConfiguration()
    {
        var settings = new DomainJoinSettings
        {
            IsEnabled = true,
            Mode = DomainJoinMode.Interactive,
            AllowDomainSelectionDuringDeployment = true,
            AllowOuSelectionDuringDeployment = true
        };
        Assert.True(DomainJoinConfigurationValidator.EvaluateReadiness(settings, _ => false, false).IsValid);
    }

    [Fact]
    public void ZeroTouchNeedsAtLeastOneDomain()
    {
        var settings = new DomainJoinSettings { IsEnabled = true, Mode = DomainJoinMode.Automatic, SharedAccountName = "CORP\\join" };
        Assert.Contains(DomainJoinConfigurationValidator.EvaluateReadiness(settings, _ => true, true).Issues,
            issue => issue.Code == DomainJoinValidationCode.DomainsRequired);
    }

    [Fact]
    public void ZeroTouchReportsEachDomainWithoutAnAccount()
    {
        DomainJoinSettings settings = ZeroTouch(null, Domain("a", "corp.test"), Domain("b", "emea.test") with { AccountName = "EMEA\\join" });
        IReadOnlyList<DomainJoinValidationIssue> issues = DomainJoinConfigurationValidator.EvaluateReadiness(settings, _ => true, true).Issues;
        Assert.Contains(issues, issue => issue.Code == DomainJoinValidationCode.SharedAccountRequired && issue.DomainId == "a");
        Assert.DoesNotContain(issues, issue => issue.DomainId == "b");
    }

    [Fact]
    public void ZeroTouchReportsADomainWhoseAccountHasNoPassword()
    {
        DomainJoinSettings settings = ZeroTouch("CORP\\join", Domain("a", "corp.test"), Domain("b", "emea.test") with { AccountName = "EMEA\\join" });
        DomainJoinValidationResult result = DomainJoinConfigurationValidator.EvaluateReadiness(settings,
            account => string.Equals(account, "CORP\\join", StringComparison.OrdinalIgnoreCase), true);
        DomainJoinValidationIssue issue = Assert.Single(result.Issues);
        Assert.Equal(DomainJoinValidationCode.PasswordRequired, issue.Code);
        Assert.Equal("b", issue.DomainId);
    }

    [Theory]
    [InlineData("technician")]
    [InlineData("CONTOSO\\")]
    [InlineData("@contoso.test")]
    [InlineData("a@b@c")]
    public void ZeroTouchRequiresAQualifiedAccount(string account)
    {
        DomainJoinSettings settings = ZeroTouch(account, Domain("a", "corp.test"));
        Assert.True(DomainJoinConfigurationValidator.ValidateMetadata(settings).IsValid);
        Assert.Contains(DomainJoinConfigurationValidator.EvaluateReadiness(settings, _ => true, true).Issues,
            issue => issue.Code == DomainJoinValidationCode.QualifiedAccountRequired && issue.DomainId == "a");
    }

    [Theory]
    [InlineData("CONTOSO\\technician")]
    [InlineData("technician@contoso.test")]
    public void ZeroTouchWithQualifiedAccountPasswordAndProtectedMediaIsReady(string account)
    {
        Assert.True(DomainJoinConfigurationValidator.EvaluateReadiness(ZeroTouch(account, Domain("a", "corp.test")), _ => true, true).IsValid);
    }

    [Fact]
    public void ZeroTouchNeedsProtectedMedia()
    {
        DomainJoinValidationIssue issue = Assert.Single(DomainJoinConfigurationValidator.EvaluateReadiness(
            ZeroTouch("CORP\\join", Domain("a", "corp.test")), _ => true, false).Issues);
        Assert.Equal(DomainJoinValidationCode.MediaProtectionRequired, issue.Code);
        Assert.Null(issue.DomainId);
    }

    [Fact]
    public void InteractiveIsReadyWithoutDomains()
    {
        var settings = new DomainJoinSettings { IsEnabled = true, Mode = DomainJoinMode.Interactive };
        Assert.True(DomainJoinConfigurationValidator.EvaluateReadiness(settings, _ => false, false).IsValid);
    }

    [Fact]
    public void ContradictoryModesAreRejected()
    {
        Assert.Throws<InvalidOperationException>(() => DomainJoinConfigurationValidator.ThrowIfProvisioningModesConflict(
            new AutopilotSettings { IsEnabled = true }, ZeroTouch("CORP\\join", Domain("a", "corp.test"))));
    }

    [Fact]
    public void MetadataRejectsMaximumPlusOneBoundsAndEmbeddedNull()
    {
        DomainJoinOrganizationalUnitSettings unit = Unit("one", "OU=Sales,DC=corp,DC=test");
        foreach ((DomainJoinSettings invalid, DomainJoinValidationCode code) in new[]
        {
            (With(Domain("a", new string('a', 254))), DomainJoinValidationCode.InvalidDomainName),
            (With(Domain("a", "bad_domain.test")), DomainJoinValidationCode.InvalidDomainName),
            (With(Domain(new string('i', 129), "corp.test")), DomainJoinValidationCode.InvalidDomainId),
            (With(Domain("a", "corp.test")) with { SharedAccountName = new string('a', 513) }, DomainJoinValidationCode.InvalidAccountName),
            (With(Domain("a", "corp.test") with { AccountName = "domain\\a\0b" }), DomainJoinValidationCode.InvalidAccountName),
            (With(Domain("a", "corp.test", Enumerable.Range(0, 1025).Select(i => Unit(i.ToString(), $"OU=OU{i},DC=corp,DC=test")).ToArray())), DomainJoinValidationCode.TooManyOrganizationalUnits),
            (With(Domain("a", "corp.test", unit with { Id = new string('a', 129) })), DomainJoinValidationCode.InvalidOuId),
            (With(Domain("a", "corp.test", unit with { DisplayName = new string('a', 121) })), DomainJoinValidationCode.InvalidOuDisplayName),
            (With(Domain("a", "corp.test", unit with { DistinguishedName = "OU=" + new string('a', 4097) })), DomainJoinValidationCode.InvalidDistinguishedName),
            (With(Domain("a", "corp.test", unit with { DistinguishedName = "CN=Computers,DC=corp,DC=test" })), DomainJoinValidationCode.InvalidDistinguishedName)
        })
        {
            DomainJoinValidationResult result = DomainJoinConfigurationValidator.ValidateMetadata(invalid);
            Assert.False(result.IsValid);
            Assert.Contains(result.Issues, issue => issue.Code == code);
        }
    }

    [Fact]
    public void MetadataAcceptsExactBounds()
    {
        string id = new('i', 128);
        string suffix = ",DC=corp,DC=test";
        string dn = "OU=" + new string('a', 4096 - 3 - suffix.Length) + suffix;
        DomainJoinSettings settings = With(Domain(id, "corp.test", new DomainJoinOrganizationalUnitSettings
        { Id = id, DisplayName = new string('a', 120), DistinguishedName = dn }) with
        { DefaultOuId = id, AccountName = new string('a', 512) });
        Assert.True(DomainJoinConfigurationValidator.ValidateMetadata(settings).IsValid);
        settings = With(Domain("a", "corp.test", Enumerable.Range(0, 1024).Select(i => Unit(i.ToString(), $"OU=OU{i},DC=corp,DC=test")).ToArray()));
        Assert.True(DomainJoinConfigurationValidator.ValidateMetadata(settings).IsValid);
        string maximumDomain = string.Join('.', new string('a', 63), new string('b', 63), new string('c', 63), new string('d', 61));
        Assert.True(DomainJoinConfigurationValidator.EvaluateReadiness(ZeroTouch("CORP\\join", Domain("a", maximumDomain)), _ => true, true).IsValid);
    }

    [Fact]
    public void ReferencedAccountsAreDistinctResolvedAndOnlyForZeroTouch()
    {
        DomainJoinSettings settings = ZeroTouch("CORP\\join", Domain("a", "corp.test"), Domain("b", "emea.test"),
            Domain("c", "lab.test") with { AccountName = "LAB\\Join" });
        Assert.Equal(new[] { DomainJoinCredentialContext.CanonicalizeAccountName("CORP\\join"), DomainJoinCredentialContext.CanonicalizeAccountName("LAB\\Join") },
            settings.GetReferencedAccountNames());
        // The shared account keeps its password while it is being set up, before any domain relies on it.
        Assert.Equal([DomainJoinCredentialContext.CanonicalizeAccountName("CORP\\join")], (settings with { Domains = [] }).GetReferencedAccountNames());
        Assert.Empty((settings with { Mode = DomainJoinMode.Interactive }).GetReferencedAccountNames());
        Assert.Empty((settings with { IsEnabled = false }).GetReferencedAccountNames());
        Assert.Equal("LAB\\Join", settings.ResolveAccountName(settings.Domains[2]));
        Assert.Equal("CORP\\join", settings.ResolveAccountName(settings.Domains[0]));
        Assert.Same(settings.Domains[1], settings.FindDomain("B"));
    }

    [Fact]
    public void CredentialOwnershipCanonicalizesDomainAndAccountButRejectsChangedOwners()
    {
        var context = new DomainJoinCredentialContext("Contoso.TEST.", "contoso\\Technician");
        Assert.True(context.Matches(new("contoso.test", "CONTOSO\\technician")));
        Assert.False(context.Matches(new("other.test", "CONTOSO\\technician")));
        Assert.False(context.Matches(new("contoso.test", "CONTOSO\\other")));
    }

    private static DomainJoinDomainSettings Domain(string id, string name, params DomainJoinOrganizationalUnitSettings[] units) =>
        new() { Id = id, DomainName = name, OrganizationalUnits = units };

    private static DomainJoinOrganizationalUnitSettings Unit(string id, string distinguishedName) =>
        new() { Id = id, DisplayName = "Computers", DistinguishedName = distinguishedName };

    private static DomainJoinSettings With(params DomainJoinDomainSettings[] domains) =>
        new() { Domains = domains, DefaultDomainId = domains.FirstOrDefault()?.Id };

    private static DomainJoinSettings ZeroTouch(string? shared, params DomainJoinDomainSettings[] domains) =>
        With(domains) with { IsEnabled = true, Mode = DomainJoinMode.Automatic, SharedAccountName = shared };
}
