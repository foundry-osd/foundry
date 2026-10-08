// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Reflection;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;

namespace Foundry.Core.Tests.Configuration;

public sealed class DomainJoinSecretStateTests
{
    private static readonly DomainJoinCredentialContext Context = new("example.com", "EXAMPLE\\joiner");

    [Fact]
    public void CodecPreservesPasswordAndClearsDecodedBuffer()
    {
        byte[] encoded = DomainJoinCredentialPayloadCodec.Encode(Context, " exact password ");
        using DomainJoinCredentialPayload decoded = DomainJoinCredentialPayloadCodec.Decode(encoded, new("EXAMPLE.COM.", "example\\JOINER"));
        ReadOnlyMemory<char> owned = decoded.Password;
        Assert.Equal(" exact password ", new string(owned.Span));
        decoded.Dispose();
        Assert.All(owned.ToArray(), value => Assert.Equal('\0', value));
    }

    [Fact]
    public void CodecRejectsContextSubstitutionAndMalformedPayload()
    {
        byte[] encoded = DomainJoinCredentialPayloadCodec.Encode(Context, "password");
        Assert.Throws<System.Security.Cryptography.CryptographicException>(() => DomainJoinCredentialPayloadCodec.Decode(encoded, new("other.com", Context.AccountName)));
        Assert.Throws<System.Security.Cryptography.CryptographicException>(() => DomainJoinCredentialPayloadCodec.Decode(encoded, new("example.com", "EXAMPLE\\other")));
        Assert.Throws<InvalidDataException>(() => DomainJoinCredentialPayloadCodec.Decode(encoded.AsSpan(0, encoded.Length - 1)));
        Assert.Throws<InvalidDataException>(() => DomainJoinCredentialPayloadCodec.Decode(new byte[32769]));
    }

    [Theory]
    [InlineData("\0")]
    public void CodecRejectsUnsafePassword(string password)
    {
        Assert.Throws<InvalidDataException>(() => DomainJoinCredentialPayloadCodec.Encode(Context, password));
    }

    [Fact]
    public void CodecRejectsUnpairedSurrogate()
    {
        string password = new('\ud800', 1);
        Assert.Throws<InvalidDataException>(() => DomainJoinCredentialPayloadCodec.Encode(Context, password));
    }

    [Fact]
    public void CodecRejectsTrailingBytesAndInvalidUtf8()
    {
        byte[] encoded = DomainJoinCredentialPayloadCodec.Encode(Context, "password");
        Assert.Throws<InvalidDataException>(() => DomainJoinCredentialPayloadCodec.Decode([.. encoded, 0]));
        encoded[^1] = 0xff;
        Assert.Throws<InvalidDataException>(() => DomainJoinCredentialPayloadCodec.Decode(encoded));
    }

    [Fact]
    public void CodecEnforcesPasswordUtf8Bound()
    {
        Assert.Throws<InvalidDataException>(() => DomainJoinCredentialPayloadCodec.Encode(Context, new string('é', 1281)));
        using var state = new DomainJoinSecretState();
        Assert.Throws<InvalidDataException>(() => state.SetPassword(Account, new string('a', 2561)));
    }

    [Fact]
    public void TwoDomainsNamingTheSameAccountShareOnePassword()
    {
        using var state = new DomainJoinSecretState();
        state.SetPassword("CORP\\join", " exact password ");
        Assert.True(state.HasPassword("corp\\JOIN"));
        Assert.Equal(" exact password ", new string(state.GetPasswordCopy("corp\\JOIN")!));
        Assert.Single(state.AccountNames);
    }

    [Fact]
    public void AnAccountStillUsedByAnotherDomainKeepsItsPassword()
    {
        using var state = new DomainJoinSecretState();
        state.SetPassword("CORP\\join", "shared");
        DomainJoinSettings settings = ZeroTouch("CORP\\join", new DomainJoinDomainSettings { Id = "a", DomainName = "corp.test", AccountName = "LAB\\join" },
            new DomainJoinDomainSettings { Id = "b", DomainName = "emea.test" });
        Assert.False(state.Update(settings));
        Assert.True(state.HasPassword("CORP\\join"));
    }

    [Fact]
    public void AnAccountNoLongerReferencedLosesItsPassword()
    {
        using var state = new DomainJoinSecretState();
        state.SetPassword("CORP\\join", "shared");
        state.SetPassword("LAB\\join", "dedicated");
        Assert.True(state.Update(ZeroTouch("CORP\\join", new DomainJoinDomainSettings { Id = "a", DomainName = "corp.test" })));
        Assert.True(state.HasPassword("CORP\\join"));
        Assert.False(state.HasPassword("LAB\\join"));
        Assert.Null(state.GetPasswordCopy("LAB\\join"));
    }

    [Theory]
    [InlineData(false, DomainJoinMode.Automatic)]
    [InlineData(true, DomainJoinMode.Interactive)]
    public void InteractiveOrDisabledSettingsEraseEveryPassword(bool enabled, DomainJoinMode mode)
    {
        using var state = new DomainJoinSecretState();
        state.SetPassword(Account, "password");
        Assert.True(state.Update(ZeroTouch(Account, new DomainJoinDomainSettings { Id = "a", DomainName = "example.com" }) with { IsEnabled = enabled, Mode = mode }));
        Assert.False(state.HasPassword(Account));
        Assert.Empty(state.AccountNames);
    }

    [Theory]
    [InlineData("join")]
    [InlineData("")]
    [InlineData("CORP\\")]
    public void AnUnqualifiedAccountCannotOwnAPassword(string account)
    {
        using var state = new DomainJoinSecretState();
        Assert.Throws<ArgumentException>(() => state.SetPassword(account, "password"));
        Assert.False(state.HasPassword(account));
    }

    [Fact]
    public void AnEmptyValueRemovesTheAccountsPassword()
    {
        using var state = new DomainJoinSecretState();
        state.SetPassword(Account, "password");
        state.SetPassword(Account, string.Empty);
        Assert.False(state.HasPassword(Account));
    }

    [Fact]
    public void ReturnedCopiesAreIndependent()
    {
        using var state = new DomainJoinSecretState();
        state.SetPassword(Account, "password");
        char[] first = state.GetPasswordCopy(Account)!;
        Array.Clear(first);
        Assert.Equal("password", new string(state.GetPasswordCopy(Account)!));
    }

    [Fact]
    public void OwnedCopiesAreZeroedOnDispose()
    {
        var state = new DomainJoinSecretState();
        char[] input = "password".ToCharArray();
        state.SetPassword(Account, input);
        var owned = (Dictionary<string, char[]>)typeof(DomainJoinSecretState).GetField("passwords", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(state)!;
        char[] buffer = owned.Values.Single();
        state.Dispose();
        Assert.All(buffer, value => Assert.Equal('\0', value));
        Assert.Equal("password", new string(input));
        Assert.Throws<ObjectDisposedException>(() => state.GetPasswordCopy(Account));
    }

    private const string Account = "EXAMPLE\\joiner";

    private static DomainJoinSettings ZeroTouch(string? shared, params DomainJoinDomainSettings[] domains) => new()
    {
        IsEnabled = true,
        Mode = DomainJoinMode.Automatic,
        SharedAccountName = shared,
        Domains = domains,
        DefaultDomainId = domains[0].Id
    };
}
