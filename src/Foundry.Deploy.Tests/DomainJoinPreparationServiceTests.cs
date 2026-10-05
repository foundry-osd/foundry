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
    public void InteractiveWithoutCatalogAcceptsOptionalDestination(string? destinationDn)
    {
        using var keys = new DeploymentSecretKeySession();
        using var submission = new DomainJoinSubmission("corp.test", "CORP\\join", null, "secret".AsSpan(), destinationDn);
        var service = new DomainJoinPreparationService(keys);

        using DomainJoinPreparationResult result = service.Prepare(new() { IsEnabled = true }, "LAB-01", submission);

        Assert.Equal(DomainJoinPreparationStatus.Ready, result.Status);
        Assert.Equal(string.IsNullOrEmpty(destinationDn) ? null : destinationDn, result.Input!.TargetOuDn);
    }

    public static TheoryData<string> InvalidDestinations => new()
    {
        "not-a-dn", "OU=Field,DC=other,DC=test", "OU=Field\0,DC=corp,DC=test", "OU=" + new string('x', 4096) + ",DC=corp,DC=test"
    };

    [Theory]
    [MemberData(nameof(InvalidDestinations))]
    public void InteractiveRejectsInvalidOrForeignDestination(string destinationDn)
    {
        using var keys = new DeploymentSecretKeySession();
        using var submission = new DomainJoinSubmission("corp.test", "CORP\\join", null, "secret".AsSpan(), destinationDn);
        var service = new DomainJoinPreparationService(keys);

        using DomainJoinPreparationResult result = service.Prepare(new() { IsEnabled = true }, "LAB-01", submission);

        Assert.Equal(DomainJoinPreparationStatus.Invalid, result.Status);
        Assert.Null(result.Input);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CompatibleCatalogRejectsArbitraryDestination(bool automatic, bool pickerEnabled)
    {
        using var keys = new DeploymentSecretKeySession();
        byte[] key = new byte[32];
        keys.SetKey(key);
        DeployDomainJoinSettings settings = WithCatalog(automatic ? Automatic(new("corp.test", "CORP\\join"), key) : new() { IsEnabled = true }) with
        { AllowOuSelectionDuringDeployment = pickerEnabled, DefaultOuId = "sales" };
        using var submission = new DomainJoinSubmission("corp.test", "CORP\\join", "sales", "secret".AsSpan(), "OU=Unlisted,DC=corp,DC=test");
        var service = new DomainJoinPreparationService(keys);

        using DomainJoinPreparationResult result = service.Prepare(settings, "LAB-01", submission);

        Assert.Equal(DomainJoinPreparationStatus.Invalid, result.Status);
        Assert.Null(result.Input);
    }

    [Theory]
    [InlineData("OU=Field,DC=other,DC=test", true)]
    [InlineData("OU=Sales,DC=corp,DC=test", false)]
    public void ChangedDomainSuppressesCatalogAndValidatesSubmittedDestination(string destinationDn, bool valid)
    {
        using var keys = new DeploymentSecretKeySession();
        using var submission = new DomainJoinSubmission("other.test", "OTHER\\join", "sales", "secret".AsSpan(), destinationDn);
        var service = new DomainJoinPreparationService(keys);
        DeployDomainJoinSettings settings = WithCatalog(new() { IsEnabled = true }) with { DefaultOuId = "sales" };

        using DomainJoinPreparationResult result = service.Prepare(settings, "LAB-01", submission);

        Assert.Equal(valid ? DomainJoinPreparationStatus.Ready : DomainJoinPreparationStatus.Invalid, result.Status);
        if (valid) Assert.Equal(destinationDn, result.Input!.TargetOuDn);
        else Assert.Null(result.Input);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(false, "sales")]
    [InlineData(true, null)]
    [InlineData(true, "sales")]
    public void CompatiblePickerRequiresSubmittedCatalogSelection(bool automatic, string? defaultOuId)
    {
        using var keys = new DeploymentSecretKeySession();
        byte[] key = new byte[32];
        keys.SetKey(key);
        DeployDomainJoinSettings settings = WithCatalog(automatic ? Automatic(new("corp.test", "CORP\\join"), key) : new() { IsEnabled = true }) with
        { DefaultOuId = defaultOuId };
        using var submission = new DomainJoinSubmission("corp.test", "CORP\\join", null, "secret".AsSpan());
        var service = new DomainJoinPreparationService(keys);

        using DomainJoinPreparationResult result = service.Prepare(settings, "LAB-01", submission);

        Assert.Equal(DomainJoinPreparationStatus.Invalid, result.Status);
        Assert.Null(result.Input);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompatiblePickerAcceptsSubmittedPreselectedDefault(bool automatic)
    {
        using var keys = new DeploymentSecretKeySession();
        byte[] key = new byte[32];
        keys.SetKey(key);
        DeployDomainJoinSettings settings = WithCatalog(automatic ? Automatic(new("corp.test", "CORP\\join"), key) : new() { IsEnabled = true }) with
        { DefaultOuId = "sales" };
        using var submission = new DomainJoinSubmission("corp.test", "CORP\\join", "sales", "secret".AsSpan());
        var service = new DomainJoinPreparationService(keys);

        using DomainJoinPreparationResult result = service.Prepare(settings, "LAB-01", submission);

        Assert.Equal(DomainJoinPreparationStatus.Ready, result.Status);
        Assert.Equal("OU=Sales,DC=corp,DC=test", result.Input!.TargetOuDn);
    }

    [Theory]
    [InlineData(false, null, null)]
    [InlineData(false, "sales", "OU=Sales,DC=corp,DC=test")]
    [InlineData(true, null, null)]
    [InlineData(true, "sales", "OU=Sales,DC=corp,DC=test")]
    public void NonPickerPreservesConfiguredDestination(bool automatic, string? defaultOuId, string? expectedDn)
    {
        using var keys = new DeploymentSecretKeySession();
        byte[] key = new byte[32];
        keys.SetKey(key);
        DeployDomainJoinSettings settings = WithCatalog(automatic ? Automatic(new("corp.test", "CORP\\join"), key) : new() { IsEnabled = true }) with
        { AllowOuSelectionDuringDeployment = false, DefaultOuId = defaultOuId };
        using var submission = new DomainJoinSubmission("corp.test", "CORP\\join", null, "secret".AsSpan());
        var service = new DomainJoinPreparationService(keys);

        using DomainJoinPreparationResult result = service.Prepare(settings, "LAB-01", submission);

        Assert.Equal(DomainJoinPreparationStatus.Ready, result.Status);
        Assert.Equal(expectedDn, result.Input!.TargetOuDn);
    }

    internal static DeployDomainJoinSettings WithCatalog(DeployDomainJoinSettings settings) => settings with
    {
        DomainName = "corp.test",
        OuCatalogDomain = "corp.test",
        AllowOuSelectionDuringDeployment = true,
        OrganizationalUnits = [new() { Id = "sales", DisplayName = "Sales", DistinguishedName = "OU=Sales,DC=corp,DC=test" }]
    };

    [Fact]
    public void MalformedEnvelopeReturnsAllowlistedFailure()
    {
        using var keys = new DeploymentSecretKeySession();
        byte[] key = new byte[32];
        keys.SetKey(key);
        DeployDomainJoinSettings settings = Automatic(new("corp.test", "CORP\\join"), key);
        settings = settings with { EncryptedCredentials = settings.EncryptedCredentials! with { Ciphertext = "!invalid!" } };
        DomainJoinSubmission? submission = null;
        var service = new DomainJoinPreparationService(keys);
        using DomainJoinPreparationResult result = service.Prepare(settings, "LAB-01", submission);
        Assert.Equal(DomainJoinPreparationStatus.Invalid, result.Status);
        Assert.Equal(DomainJoinPreparationFailure.CredentialsInvalid, result.FailureCode);
        Assert.Null(result.Input);
    }

    [Fact]
    public void CredentialEnvelopeIsBoundedBeforeObtainingKeyCopy()
    {
        var keys = new RecordingKeys();
        DeployDomainJoinSettings settings = Automatic(new("corp.test", "CORP\\join"), new byte[32]);
        settings = settings with { EncryptedCredentials = settings.EncryptedCredentials! with { Ciphertext = new string('A', 50000) } };
        DomainJoinSubmission? submission = null;
        var service = new DomainJoinPreparationService(keys);
        using DomainJoinPreparationResult result = service.Prepare(settings, "LAB-01", submission);
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
        using var submission = new DomainJoinSubmission("corp.test", "CORP\\join", null, " secret ".AsSpan());
        var service = new DomainJoinPreparationService(keys);

        using DomainJoinPreparationResult result = service.Prepare(new() { IsEnabled = true }, "LAB-01", submission);

        Assert.Equal(DomainJoinPreparationStatus.Ready, result.Status);
        Assert.Equal("corp.test", result.Input!.CredentialContext.DomainName);
        Assert.Equal("LAB-01", result.Input.ComputerName);
        Assert.Equal(" secret ", new string(result.Input.Password.Span));
        Assert.False(keys.IsUnlocked);
    }

    [Fact]
    public void InteractiveWithoutWizardInputNeverProducesInput()
    {
        using var keys = new DeploymentSecretKeySession();
        DomainJoinSubmission? submission = null;
        var service = new DomainJoinPreparationService(keys);

        using DomainJoinPreparationResult result = service.Prepare(new() { IsEnabled = true }, "LAB-01", submission);

        Assert.Equal(DomainJoinPreparationStatus.Invalid, result.Status);
        Assert.Equal(DomainJoinPreparationFailure.CredentialsInvalid, result.FailureCode);
        Assert.Null(result.Input);
    }

    [Fact]
    public void AutomaticUsesExistingUnlockOnly()
    {
        using var keys = new DeploymentSecretKeySession();
        byte[] key = RandomNumberGenerator.GetBytes(32);
        keys.SetKey(key);
        var context = new DomainJoinCredentialContext("corp.test", "CORP\\join");
        DeployDomainJoinSettings settings = Automatic(context, key);
        DomainJoinSubmission? submission = null;
        var service = new DomainJoinPreparationService(keys);

        using DomainJoinPreparationResult result = service.Prepare(settings, "LAB-01", submission);

        Assert.Equal(DomainJoinPreparationStatus.Ready, result.Status);
        Assert.Equal(" secret ", new string(result.Input!.Password.Span));
        Assert.True(keys.IsUnlocked);
        CryptographicOperations.ZeroMemory(key);
    }

    [Theory]
    [InlineData("other.test", "CORP\\join")]
    [InlineData("corp.test", "CORP\\different")]
    public void AutomaticContextMismatchNeverAuthenticates(string domain, string account)
    {
        using var keys = new DeploymentSecretKeySession();
        byte[] key = RandomNumberGenerator.GetBytes(32);
        keys.SetKey(key);
        DeployDomainJoinSettings settings = Automatic(new("corp.test", "CORP\\join"), key) with
        { DomainName = domain, AccountName = account };
        DomainJoinSubmission? submission = null;
        var service = new DomainJoinPreparationService(keys);

        using DomainJoinPreparationResult result = service.Prepare(settings, "LAB-01", submission);

        Assert.Equal(DomainJoinPreparationStatus.Invalid, result.Status);
        Assert.Equal(DomainJoinPreparationFailure.CredentialsInvalid, result.FailureCode);
        Assert.Null(result.Input);
        CryptographicOperations.ZeroMemory(key);
    }

    [Fact]
    public void ChangedDomainClearsCatalogDestination()
    {
        using var keys = new DeploymentSecretKeySession();
        using var submission = new DomainJoinSubmission("other.test", "OTHER\\join", "sales", "secret".AsSpan());
        var service = new DomainJoinPreparationService(keys);
        using DomainJoinPreparationResult result = service.Prepare(new()
        {
            IsEnabled = true,
            DomainName = "corp.test",
            OuCatalogDomain = "corp.test",
            DefaultOuId = "sales",
            AllowOuSelectionDuringDeployment = true,
            OrganizationalUnits = [new() { Id = "sales", DisplayName = "Sales", DistinguishedName = "OU=Sales,DC=corp,DC=test" }]
        }, "LAB-01", submission);
        Assert.Equal(DomainJoinPreparationStatus.Ready, result.Status);
        Assert.Null(result.Input!.TargetOuDn);
    }

    [Theory]
    [InlineData("", "CORP\\join", "secret")]
    [InlineData("corp.test", "", "secret")]
    [InlineData("corp.test", "join", "secret")]
    [InlineData("corp.test", "CORP\\join", "")]
    public void MissingOrInvalidInteractiveCredentialsFailClosed(string domain, string account, string password)
    {
        using var keys = new DeploymentSecretKeySession();
        using var submission = new DomainJoinSubmission(domain, account, null, password.AsSpan());
        var service = new DomainJoinPreparationService(keys);
        using DomainJoinPreparationResult result = service.Prepare(new() { IsEnabled = true }, "LAB-01", submission);
        Assert.Equal(DomainJoinPreparationStatus.Invalid, result.Status);
        Assert.Null(result.Input);
    }

    [Theory]
    [InlineData("corp.test", "corp.test", "sales", "OU=Sales,DC=corp,DC=test")]
    [InlineData("corp.test", "corp.test", null, null)]
    [InlineData("corp.test", "other.test", "sales", null)]
    [InlineData(null, "corp.test", "sales", null)]
    public void DefaultOrganizationalUnitAppliesOnlyToTheDomainOfTheSavedList(string? domain, string catalogDomain, string? defaultOuId,
        string? expectedDefaultDn)
    {
        var settings = new DeployDomainJoinSettings
        {
            IsEnabled = true,
            DomainName = domain,
            OuCatalogDomain = catalogDomain,
            DefaultOuId = defaultOuId,
            OrganizationalUnits = [new() { Id = "sales", DisplayName = "Sales", DistinguishedName = "OU=Sales,DC=corp,DC=test" }]
        };
        Assert.Equal(expectedDefaultDn, DomainJoinPreparationService.ResolveDefaultOrganizationalUnit(settings, domain)?.DistinguishedName);
    }

    internal static DeployDomainJoinSettings Automatic(DomainJoinCredentialContext context, byte[] key)
    {
        byte[] payload = DomainJoinCredentialPayloadCodec.Encode(context, " secret ".AsSpan());
        try
        {
            return new()
            {
                IsEnabled = true,
                Mode = DomainJoinMode.Automatic,
                DomainName = context.DomainName,
                AccountName = context.AccountName,
                EncryptedCredentials = MediaSecretEnvelopeProtector.EncryptBytes(payload, key, MediaSecretEnvelopeProtector.DeploymentKeyId)
            };
        }
        finally { CryptographicOperations.ZeroMemory(payload); }
    }
}
