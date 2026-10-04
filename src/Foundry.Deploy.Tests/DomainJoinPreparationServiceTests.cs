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
    [Fact]
    public void MalformedEnvelopeReturnsAllowlistedFailure()
    {
        using var keys = new DeploymentSecretKeySession();
        byte[] key = new byte[32];
        keys.SetKey(key);
        DeployDomainJoinSettings settings = Automatic(new("corp.test", "CORP\\join"), key);
        settings = settings with { EncryptedCredentials = settings.EncryptedCredentials! with { Ciphertext = "!invalid!" } };
        var service = new DomainJoinPreparationService(new Dialog(null), keys);
        using DomainJoinPreparationResult result = service.Prepare(settings, "LAB-01");
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
        var service = new DomainJoinPreparationService(new Dialog(null), keys);
        using DomainJoinPreparationResult result = service.Prepare(settings, "LAB-01");
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
    public void ManualOnUnprotectedMediaPromptsBeforeErase()
    {
        using var keys = new DeploymentSecretKeySession();
        var dialog = new Dialog(new("corp.test", "CORP\\join", null, " secret ".AsSpan()));
        var service = new DomainJoinPreparationService(dialog, keys);

        using DomainJoinPreparationResult result = service.Prepare(new() { IsEnabled = true }, "LAB-01");

        Assert.Equal(DomainJoinPreparationStatus.Ready, result.Status);
        Assert.Equal("corp.test", result.Input!.CredentialContext.DomainName);
        Assert.Equal("LAB-01", result.Input.ComputerName);
        Assert.Equal(" secret ", new string(result.Input.Password.Span));
        Assert.False(keys.IsUnlocked);
        Assert.True(dialog.CredentialsRequested);
    }

    [Fact]
    public void CanceledCredentialOrOuDialogNeverProducesInput()
    {
        using var keys = new DeploymentSecretKeySession();
        var service = new DomainJoinPreparationService(new Dialog(null), keys);

        using DomainJoinPreparationResult result = service.Prepare(new() { IsEnabled = true }, "LAB-01");

        Assert.Equal(DomainJoinPreparationStatus.Canceled, result.Status);
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
        var dialog = new Dialog(null);
        var service = new DomainJoinPreparationService(dialog, keys);

        using DomainJoinPreparationResult result = service.Prepare(settings, "LAB-01");

        Assert.Equal(DomainJoinPreparationStatus.Ready, result.Status);
        Assert.Equal(" secret ", new string(result.Input!.Password.Span));
        Assert.False(dialog.WasShown);
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
        var service = new DomainJoinPreparationService(new Dialog(null), keys);

        using DomainJoinPreparationResult result = service.Prepare(settings, "LAB-01");

        Assert.Equal(DomainJoinPreparationStatus.Invalid, result.Status);
        Assert.Equal(DomainJoinPreparationFailure.CredentialsInvalid, result.FailureCode);
        Assert.Null(result.Input);
        CryptographicOperations.ZeroMemory(key);
    }

    [Fact]
    public void ChangedDomainClearsCatalogDestination()
    {
        using var keys = new DeploymentSecretKeySession();
        var service = new DomainJoinPreparationService(new Dialog(new("other.test", "OTHER\\join", "sales", "secret".AsSpan())), keys);
        using DomainJoinPreparationResult result = service.Prepare(new()
        {
            IsEnabled = true,
            DomainName = "corp.test",
            OuCatalogDomain = "corp.test",
            DefaultOuId = "sales",
            AllowOuSelectionDuringDeployment = true,
            OrganizationalUnits = [new() { Id = "sales", DisplayName = "Sales", DistinguishedName = "OU=Sales,DC=corp,DC=test" }]
        }, "LAB-01");
        Assert.Equal(DomainJoinPreparationStatus.Ready, result.Status);
        Assert.Null(result.Input!.TargetOuDn);
    }

    [Theory]
    [InlineData("", "CORP\\join", "secret")]
    [InlineData("corp.test", "", "secret")]
    [InlineData("corp.test", "join", "secret")]
    [InlineData("corp.test", "CORP\\join", "")]
    public void MissingOrInvalidManualCredentialsFailClosed(string domain, string account, string password)
    {
        using var keys = new DeploymentSecretKeySession();
        var service = new DomainJoinPreparationService(new Dialog(new(domain, account, null, password.AsSpan())), keys);
        using DomainJoinPreparationResult result = service.Prepare(new() { IsEnabled = true }, "LAB-01");
        Assert.Equal(DomainJoinPreparationStatus.Invalid, result.Status);
        Assert.Null(result.Input);
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

    internal sealed class Dialog(DomainJoinDialogResult? result) : IDomainJoinDialogService
    {
        public bool WasShown { get; private set; }
        public bool CredentialsRequested { get; private set; }
        public DomainJoinDialogResult? Show(DeployDomainJoinSettings settings, bool requiresCredentials)
        {
            WasShown = true;
            CredentialsRequested = requiresCredentials;
            return result;
        }
    }
}
