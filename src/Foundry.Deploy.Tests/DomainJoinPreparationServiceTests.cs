// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Core.Services.Autopilot;
using Foundry.Core.Services.Configuration;
using Foundry.Deploy.Services.DomainJoin;
using Foundry.Deploy.Services.Security;

namespace Foundry.Deploy.Tests;

public sealed class DomainJoinPreparationServiceTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("OU=Field,DC=corp,DC=test")]
    public void InteractiveWithoutListedDomainsAcceptsATypedDomainAndOptionalOu(string? ouDn)
    {
        using DomainJoinPreparationResult result = Prepare(new() { IsEnabled = true }, Typed("corp.test", typedOu: ouDn));

        Assert.Equal(DomainJoinPreparationStatus.Ready, result.Status);
        Assert.Equal("corp.test", result.Input!.CredentialContext.DomainName);
        Assert.Equal(string.IsNullOrEmpty(ouDn) ? null : ouDn, result.Input.TargetOuDn);
    }

    public static TheoryData<string> InvalidTypedOus => new()
    {
        "not-a-dn", "OU=Field,DC=other,DC=test", "OU=Field\0,DC=corp,DC=test", "OU=" + new string('x', 4096) + ",DC=corp,DC=test", "CN=Computers,DC=corp,DC=test"
    };

    [Theory]
    [MemberData(nameof(InvalidTypedOus))]
    public void InteractiveRejectsAnInvalidOrForeignTypedOu(string ouDn)
    {
        using DomainJoinPreparationResult result = Prepare(new() { IsEnabled = true }, Typed("corp.test", typedOu: ouDn));

        Assert.Equal(DomainJoinPreparationStatus.Invalid, result.Status);
        Assert.Null(result.Input);
    }

    [Fact]
    public void InteractiveWithListedDomainsRefusesATypedDomain()
    {
        DeployDomainJoinSettings settings = TwoDomains(DomainJoinMode.Interactive);

        using DomainJoinPreparationResult refused = Prepare(settings, Typed("other.test"));
        using DomainJoinPreparationResult accepted = Prepare(settings, Typed("CORP.test", ouId: "sales"));

        Assert.Equal(DomainJoinPreparationStatus.Invalid, refused.Status);
        Assert.Null(refused.Input);
        Assert.Equal(DomainJoinPreparationStatus.Ready, accepted.Status);
        Assert.Equal("corp.test", accepted.Input!.CredentialContext.DomainName);
    }

    [Fact]
    public void InteractiveJoinsTheSelectedDomainWithTheTypedCredentials()
    {
        DeployDomainJoinSettings settings = TwoDomains(DomainJoinMode.Interactive);
        using var submission = new DomainJoinSubmission("emea", "emea.test", "EMEA\\tech", null, " secret ".AsSpan());

        using DomainJoinPreparationResult result = Prepare(settings, submission);

        Assert.Equal(DomainJoinPreparationStatus.Ready, result.Status);
        Assert.Equal("emea.test", result.Input!.CredentialContext.DomainName);
        Assert.Equal(" secret ", new string(result.Input.Password.Span));
        Assert.Null(result.Input.TargetOuDn);
    }

    [Fact]
    public void ZeroTouchWithoutASubmissionUsesTheDefaultDomainAndItsOnlyOu()
    {
        using var keys = Unlocked(out byte[] key);
        DeployDomainJoinSettings settings = TwoDomains(DomainJoinMode.Automatic, key);

        using DomainJoinPreparationResult result = new DomainJoinPreparationService(keys).Prepare(settings, "LAB-01", null);

        Assert.Equal(DomainJoinPreparationStatus.Ready, result.Status);
        Assert.Equal("corp.test", result.Input!.CredentialContext.DomainName);
        Assert.Equal("OU=Sales,DC=corp,DC=test", result.Input.TargetOuDn);
    }

    [Fact]
    public void ZeroTouchUsesTheSelectedDomainAndItsOwnPayload()
    {
        using var keys = Unlocked(out byte[] key);
        DeployDomainJoinSettings settings = TwoDomains(DomainJoinMode.Automatic, key);
        using var submission = new DomainJoinSubmission("emea", "emea.test", string.Empty, null, default);

        using DomainJoinPreparationResult result = new DomainJoinPreparationService(keys).Prepare(settings, "LAB-01", submission);

        Assert.Equal(DomainJoinPreparationStatus.Ready, result.Status);
        Assert.True(result.Input!.CredentialContext.Matches(new("emea.test", "EMEA\\join")));
        Assert.Equal("emea secret", new string(result.Input.Password.Span));
    }

    [Fact]
    public void ASelectedDomainThatIsNotListedIsRefused()
    {
        using var keys = Unlocked(out byte[] key);
        DeployDomainJoinSettings settings = TwoDomains(DomainJoinMode.Automatic, key);
        using var submission = new DomainJoinSubmission("missing", "missing.test", string.Empty, null, default);

        using DomainJoinPreparationResult result = new DomainJoinPreparationService(keys).Prepare(settings, "LAB-01", submission);

        Assert.Equal(DomainJoinPreparationStatus.Invalid, result.Status);
        Assert.Null(result.Input);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ADomainWithoutListedOusRequiresNoOu(bool automatic)
    {
        using var keys = Unlocked(out byte[] key);
        DeployDomainJoinSettings settings = TwoDomains(automatic ? DomainJoinMode.Automatic : DomainJoinMode.Interactive, key);
        using var submission = new DomainJoinSubmission("emea", "emea.test", automatic ? string.Empty : "EMEA\\tech", null,
            automatic ? default : "secret".AsSpan());

        using DomainJoinPreparationResult result = new DomainJoinPreparationService(keys).Prepare(settings, "LAB-01", submission);

        Assert.Equal(DomainJoinPreparationStatus.Ready, result.Status);
        Assert.Null(result.Input!.TargetOuDn);
    }

    [Fact]
    public void AnOuOfAnotherListedDomainIsRefused()
    {
        using var keys = Unlocked(out byte[] key);
        DeployDomainJoinSettings settings = TwoDomains(DomainJoinMode.Automatic, key);
        settings = settings with
        {
            Domains = [settings.Domains[0], settings.Domains[1] with { OrganizationalUnits = [new() { Id = "kiosk", DisplayName = "Kiosks", DistinguishedName = "OU=Kiosks,DC=emea,DC=test" }] }]
        };
        using var submission = new DomainJoinSubmission("emea", "emea.test", string.Empty, "sales", default);

        using DomainJoinPreparationResult result = new DomainJoinPreparationService(keys).Prepare(settings, "LAB-01", submission);

        Assert.Equal(DomainJoinPreparationStatus.Invalid, result.Status);
        Assert.Null(result.Input);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ADomainThatListsOusRejectsATypedOu(bool automatic)
    {
        using var keys = Unlocked(out byte[] key);
        DeployDomainJoinSettings settings = WithCatalog(automatic ? Automatic(new("corp.test", "CORP\\join"), key) : new() { IsEnabled = true });
        using var submission = new DomainJoinSubmission("corp", "corp.test", "CORP\\join", "sales", "secret".AsSpan(), "OU=Unlisted,DC=corp,DC=test");

        using DomainJoinPreparationResult result = new DomainJoinPreparationService(keys).Prepare(settings, "LAB-01", submission);

        Assert.Equal(DomainJoinPreparationStatus.Invalid, result.Status);
        Assert.Null(result.Input);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(false, "sales")]
    [InlineData(true, null)]
    [InlineData(true, "sales")]
    public void SeveralListedOusRequireASubmittedSelection(bool automatic, string? defaultOuId)
    {
        using var keys = Unlocked(out byte[] key);
        // Even a marked default is only a preselection: the join uses what the technician confirmed on the step.
        DeployDomainJoinSettings settings = WithTwoOus(WithDefaultOu(WithCatalog(automatic ? Automatic(new("corp.test", "CORP\\join"), key) : new() { IsEnabled = true }), defaultOuId));
        using var submission = new DomainJoinSubmission("corp", "corp.test", "CORP\\join", null, "secret".AsSpan());

        using DomainJoinPreparationResult result = new DomainJoinPreparationService(keys).Prepare(settings, "LAB-01", submission);

        Assert.Equal(DomainJoinPreparationStatus.Invalid, result.Status);
        Assert.Null(result.Input);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SeveralListedOusUseTheSubmittedSelection(bool automatic)
    {
        using var keys = Unlocked(out byte[] key);
        DeployDomainJoinSettings settings = WithTwoOus(WithDefaultOu(WithCatalog(automatic ? Automatic(new("corp.test", "CORP\\join"), key) : new() { IsEnabled = true }), "sales"));
        using var preselected = new DomainJoinSubmission("corp", "corp.test", "CORP\\join", "sales", "secret".AsSpan());
        using var other = new DomainJoinSubmission("corp", "corp.test", "CORP\\join", "field", "secret".AsSpan());

        using DomainJoinPreparationResult first = new DomainJoinPreparationService(keys).Prepare(settings, "LAB-01", preselected);
        using DomainJoinPreparationResult second = new DomainJoinPreparationService(keys).Prepare(settings, "LAB-01", other);

        Assert.Equal(DomainJoinPreparationStatus.Ready, first.Status);
        Assert.Equal("OU=Sales,DC=corp,DC=test", first.Input!.TargetOuDn);
        Assert.Equal("OU=Field,DC=corp,DC=test", second.Input!.TargetOuDn);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(false, "sales")]
    [InlineData(true, null)]
    [InlineData(true, "sales")]
    public void ASingleListedOuIsUsedWhetherOrNotItIsMarkedDefault(bool automatic, string? defaultOuId)
    {
        using var keys = Unlocked(out byte[] key);
        DeployDomainJoinSettings settings = WithDefaultOu(WithCatalog(automatic ? Automatic(new("corp.test", "CORP\\join"), key) : new() { IsEnabled = true }), defaultOuId);
        using var submission = new DomainJoinSubmission("corp", "corp.test", "CORP\\join", null, "secret".AsSpan());

        using DomainJoinPreparationResult result = new DomainJoinPreparationService(keys).Prepare(settings, "LAB-01", submission);

        Assert.Equal(DomainJoinPreparationStatus.Ready, result.Status);
        Assert.Equal("OU=Sales,DC=corp,DC=test", result.Input!.TargetOuDn);
    }

    [Theory]
    [InlineData(null, DomainJoinOuSource.None)]
    [InlineData("OU=Sales,DC=corp,DC=test", DomainJoinOuSource.Default)]
    [InlineData("OU=Field,DC=corp,DC=test", DomainJoinOuSource.Selected)]
    public void OuSourceTellsDefaultAndListedTargetsApart(string? targetOuDn, DomainJoinOuSource expected)
    {
        var domain = new DeployDomainJoinDomainSettings
        {
            Id = "corp",
            DomainName = "corp.test",
            DefaultOuId = "sales",
            OrganizationalUnits =
            [
                new() { Id = "sales", DisplayName = "Sales", DistinguishedName = "OU=Sales,DC=corp,DC=test" },
                new() { Id = "field", DisplayName = "Field", DistinguishedName = "OU=Field,DC=corp,DC=test" }
            ]
        };

        Assert.Equal(expected, DomainJoinPreparationService.ResolveOuSource(domain, new("corp.test", "LAB-01", targetOuDn)));
    }

    [Fact]
    public void AnOuOnADomainWithoutListedOusIsATypedOu()
    {
        var intent = new DomainJoinDeploymentIntent("emea.test", "LAB-01", "OU=Typed,DC=emea,DC=test");
        Assert.Equal(DomainJoinOuSource.Typed, DomainJoinPreparationService.ResolveOuSource(new() { Id = "emea", DomainName = "emea.test" }, intent));
        Assert.Equal(DomainJoinOuSource.Typed, DomainJoinPreparationService.ResolveOuSource(null, intent));
    }

    [Theory]
    [InlineData("corp.test", DomainJoinDomainSource.Default)]
    [InlineData("EMEA.test", DomainJoinDomainSource.Selected)]
    public void DomainSourceTellsDefaultAndSelectedApart(string joinedDomain, DomainJoinDomainSource expected)
    {
        Assert.Equal(expected, DomainJoinPreparationService.ResolveDomainSource(TwoDomains(DomainJoinMode.Interactive), new(joinedDomain, "LAB-01", null)));
    }

    [Fact]
    public void DomainSourceIsTypedWithoutListedDomainsAndNoneWithoutAJoin()
    {
        Assert.Equal(DomainJoinDomainSource.Typed, DomainJoinPreparationService.ResolveDomainSource(new() { IsEnabled = true }, new("corp.test", "LAB-01", null)));
        Assert.Equal(DomainJoinDomainSource.None, DomainJoinPreparationService.ResolveDomainSource(TwoDomains(DomainJoinMode.Interactive), null));
    }

    [Fact]
    public void DefaultOrganizationalUnitBelongsToItsDomain()
    {
        DeployDomainJoinSettings settings = TwoDomains(DomainJoinMode.Interactive);
        Assert.Equal("OU=Sales,DC=corp,DC=test", DomainJoinPreparationService.ResolveDefaultOrganizationalUnit(settings.Domains[0])?.DistinguishedName);
        Assert.Null(DomainJoinPreparationService.ResolveDefaultOrganizationalUnit(settings.Domains[1]));
        Assert.Null(DomainJoinPreparationService.ResolveDefaultOrganizationalUnit(null));
    }

    [Fact]
    public void MalformedEnvelopeReturnsAllowlistedFailure()
    {
        using var keys = Unlocked(out byte[] key);
        DeployDomainJoinSettings settings = Automatic(new("corp.test", "CORP\\join"), key);
        settings = WithEnvelope(settings, envelope => envelope with { Ciphertext = "!invalid!" });

        using DomainJoinPreparationResult result = new DomainJoinPreparationService(keys).Prepare(settings, "LAB-01", null);

        Assert.Equal(DomainJoinPreparationStatus.Invalid, result.Status);
        Assert.Equal(DomainJoinPreparationFailure.CredentialsInvalid, result.FailureCode);
        Assert.Null(result.Input);
    }

    [Fact]
    public void CredentialEnvelopeIsBoundedBeforeObtainingKeyCopy()
    {
        var keys = new RecordingKeys();
        DeployDomainJoinSettings settings = WithEnvelope(Automatic(new("corp.test", "CORP\\join"), new byte[32]),
            envelope => envelope with { Ciphertext = new string('A', 50000) });

        using DomainJoinPreparationResult result = new DomainJoinPreparationService(keys).Prepare(settings, "LAB-01", null);

        Assert.Equal(DomainJoinPreparationStatus.Invalid, result.Status);
        Assert.False(keys.CopyRequested);
    }

    private sealed class RecordingKeys : IDeploymentSecretKeySession
    {
        public bool IsUnlocked => true;
        public bool CopyRequested { get; private set; }
        public byte[] GetKeyCopy() { CopyRequested = true; return new byte[32]; }
        public void SetKey(ReadOnlySpan<byte> value) { }
        public void Clear() { }
        public void Dispose() { }
    }

    [Fact]
    public void InteractiveCollectsCredentialsWithoutMediaUnlock()
    {
        using var keys = new DeploymentSecretKeySession();
        using var submission = new DomainJoinSubmission(null, "corp.test", "CORP\\join", null, " secret ".AsSpan());

        using DomainJoinPreparationResult result = new DomainJoinPreparationService(keys).Prepare(new() { IsEnabled = true }, "LAB-01", submission);

        Assert.Equal(DomainJoinPreparationStatus.Ready, result.Status);
        Assert.Equal("corp.test", result.Input!.CredentialContext.DomainName);
        Assert.Equal("LAB-01", result.Input.ComputerName);
        Assert.Equal(" secret ", new string(result.Input.Password.Span));
        Assert.False(keys.IsUnlocked);
    }

    [Fact]
    public void InteractiveWithoutWizardInputNeverProducesInput()
    {
        using DomainJoinPreparationResult result = Prepare(new() { IsEnabled = true }, null);

        Assert.Equal(DomainJoinPreparationStatus.Invalid, result.Status);
        Assert.Equal(DomainJoinPreparationFailure.CredentialsInvalid, result.FailureCode);
        Assert.Null(result.Input);
    }

    [Fact]
    public void AutomaticUsesExistingUnlockOnly()
    {
        using var keys = Unlocked(out byte[] key);
        DeployDomainJoinSettings settings = Automatic(new("corp.test", "CORP\\join"), key);

        using DomainJoinPreparationResult result = new DomainJoinPreparationService(keys).Prepare(settings, "LAB-01", null);

        Assert.Equal(DomainJoinPreparationStatus.Ready, result.Status);
        Assert.Equal(" secret ", new string(result.Input!.Password.Span));
        Assert.True(keys.IsUnlocked);
    }

    [Fact]
    public void AutomaticWithoutUnlockAsksForIt()
    {
        using var keys = new DeploymentSecretKeySession();
        DeployDomainJoinSettings settings = Automatic(new("corp.test", "CORP\\join"), new byte[32]);

        using DomainJoinPreparationResult result = new DomainJoinPreparationService(keys).Prepare(settings, "LAB-01", null);

        Assert.Equal(DomainJoinPreparationFailure.UnlockRequired, result.FailureCode);
    }

    [Theory]
    [InlineData("other.test", "CORP\\join")]
    [InlineData("corp.test", "CORP\\different")]
    public void AutomaticContextMismatchNeverAuthenticates(string domain, string account)
    {
        using var keys = Unlocked(out byte[] key);
        DeployDomainJoinSettings settings = Automatic(new("corp.test", "CORP\\join"), key);
        settings = settings with { Domains = [settings.Domains[0] with { DomainName = domain, AccountName = account }] };

        using DomainJoinPreparationResult result = new DomainJoinPreparationService(keys).Prepare(settings, "LAB-01", null);

        Assert.Equal(DomainJoinPreparationStatus.Invalid, result.Status);
        Assert.Equal(DomainJoinPreparationFailure.CredentialsInvalid, result.FailureCode);
        Assert.Null(result.Input);
    }

    [Theory]
    [InlineData("", "CORP\\join", "secret")]
    [InlineData("corp.test", "", "secret")]
    [InlineData("corp.test", "join", "secret")]
    [InlineData("corp.test", "CORP\\join", "")]
    public void MissingOrInvalidInteractiveCredentialsFailClosed(string domain, string account, string password)
    {
        using var keys = new DeploymentSecretKeySession();
        using var submission = new DomainJoinSubmission(null, domain, account, null, password.AsSpan());

        using DomainJoinPreparationResult result = new DomainJoinPreparationService(keys).Prepare(new() { IsEnabled = true }, "LAB-01", submission);

        Assert.Equal(DomainJoinPreparationStatus.Invalid, result.Status);
        Assert.Null(result.Input);
    }

    private static DomainJoinPreparationResult Prepare(DeployDomainJoinSettings settings, DomainJoinSubmission? submission)
    {
        using var keys = new DeploymentSecretKeySession();
        try { return new DomainJoinPreparationService(keys).Prepare(settings, "LAB-01", submission); }
        finally { submission?.Dispose(); }
    }

    private static DomainJoinSubmission Typed(string domain, string? ouId = null, string? typedOu = null) =>
        new(null, domain, "CORP\\join", ouId, "secret".AsSpan(), typedOu);

    private static DeploymentSecretKeySession Unlocked(out byte[] key)
    {
        key = RandomNumberGenerator.GetBytes(32);
        var keys = new DeploymentSecretKeySession();
        keys.SetKey(key);
        return keys;
    }

    private static DeployDomainJoinSettings WithEnvelope(DeployDomainJoinSettings settings, Func<SecretEnvelope, SecretEnvelope> change) =>
        settings with { Domains = [settings.Domains[0] with { EncryptedCredentials = change(settings.Domains[0].EncryptedCredentials!) }] };

    private static DeployDomainJoinSettings WithDefaultOu(DeployDomainJoinSettings settings, string? defaultOuId) =>
        settings with { Domains = [settings.Domains[0] with { DefaultOuId = defaultOuId }] };

    /// <summary>Two listed domains: corp.test (default, one OU that is its default) and emea.test (no OU).</summary>
    internal static DeployDomainJoinSettings TwoDomains(DomainJoinMode mode, byte[]? key = null) => new()
    {
        IsEnabled = true,
        Mode = mode,
        DefaultDomainId = "corp",
        Domains =
        [
            new()
            {
                Id = "corp",
                DomainName = "corp.test",
                AccountName = mode == DomainJoinMode.Automatic ? "CORP\\join" : null,
                DefaultOuId = "sales",
                OrganizationalUnits = [new() { Id = "sales", DisplayName = "Sales", DistinguishedName = "OU=Sales,DC=corp,DC=test" }],
                EncryptedCredentials = mode == DomainJoinMode.Automatic ? Protect(new("corp.test", "CORP\\join"), "corp secret", key!) : null
            },
            new()
            {
                Id = "emea",
                DomainName = "emea.test",
                AccountName = mode == DomainJoinMode.Automatic ? "EMEA\\join" : null,
                EncryptedCredentials = mode == DomainJoinMode.Automatic ? Protect(new("emea.test", "EMEA\\join"), "emea secret", key!) : null
            }
        ]
    };

    /// <summary>Adds a second OU to the first domain, which makes the technician choose between them.</summary>
    internal static DeployDomainJoinSettings WithTwoOus(DeployDomainJoinSettings settings) => settings with
    {
        Domains =
        [
            settings.Domains[0] with
            {
                OrganizationalUnits = [.. settings.Domains[0].OrganizationalUnits, new() { Id = "field", DisplayName = "Field", DistinguishedName = "OU=Field,DC=corp,DC=test" }]
            },
            .. settings.Domains.Skip(1)
        ]
    };

    /// <summary>Lists one OU on the single domain; adds corp.test when no domain is listed.</summary>
    internal static DeployDomainJoinSettings WithCatalog(DeployDomainJoinSettings settings)
    {
        DeployDomainJoinDomainSettings domain = settings.Domains.FirstOrDefault() ?? new() { Id = "corp", DomainName = "corp.test" };
        return settings with
        {
            DefaultDomainId = domain.Id,
            Domains = [domain with { OrganizationalUnits = [new() { Id = "sales", DisplayName = "Sales", DistinguishedName = "OU=Sales,DC=corp,DC=test" }] }]
        };
    }

    /// <summary>Zero-touch media with one domain whose payload holds the password " secret ".</summary>
    internal static DeployDomainJoinSettings Automatic(DomainJoinCredentialContext context, byte[] key) => new()
    {
        IsEnabled = true,
        Mode = DomainJoinMode.Automatic,
        DefaultDomainId = "corp",
        Domains = [new() { Id = "corp", DomainName = context.DomainName, AccountName = context.AccountName, EncryptedCredentials = Protect(context, " secret ", key) }]
    };

    private static SecretEnvelope Protect(DomainJoinCredentialContext context, string password, byte[] key)
    {
        byte[] payload = DomainJoinCredentialPayloadCodec.Encode(context, password.AsSpan());
        try { return MediaSecretEnvelopeProtector.EncryptBytes(payload, key, MediaSecretEnvelopeProtector.DeploymentKeyId); }
        finally { CryptographicOperations.ZeroMemory(payload); }
    }
}
