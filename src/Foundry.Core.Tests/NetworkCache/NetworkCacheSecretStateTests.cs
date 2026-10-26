// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.NetworkCache;

namespace Foundry.Core.Tests.NetworkCache;

public sealed class NetworkCacheSecretStateTests
{
    [Fact]
    public void SetPassword_StoresIndependentCopy()
    {
        using var state = new NetworkCacheSecretState();
        char[] input = "secret".ToCharArray();

        state.SetPassword("a1", input);
        input[0] = 'X';

        Assert.Equal("secret", new string(state.GetPasswordCopy("a1")));
        Assert.True(state.HasPassword("a1"));
    }

    [Fact]
    public void SetPassword_Empty_RemovesPassword()
    {
        using var state = new NetworkCacheSecretState();
        state.SetPassword("a1", "secret");

        state.SetPassword("a1", "");

        Assert.False(state.HasPassword("a1"));
        Assert.Null(state.GetPasswordCopy("a1"));
    }

    [Fact]
    public void Update_ErasesPasswordsOfRemovedAccounts()
    {
        using var state = new NetworkCacheSecretState();
        state.SetPassword("a1", "one");
        state.SetPassword("a2", "two");

        bool changed = state.Update(new NetworkCacheSettings { Accounts = [new() { Id = "a1", UserName = "CONTOSO\\svc" }] });

        Assert.True(changed);
        Assert.True(state.HasPassword("a1"));
        Assert.False(state.HasPassword("a2"));
    }

    [Fact]
    public void Update_KeepsPasswordWhenFeatureDisabled()
    {
        using var state = new NetworkCacheSecretState();
        state.SetPassword("a1", "one");

        state.Update(new NetworkCacheSettings { IsEnabled = false, Accounts = [new() { Id = "a1", UserName = "CONTOSO\\svc" }] });

        Assert.True(state.HasPassword("a1"));
    }

    [Fact]
    public void SetPassword_TooLong_Throws()
    {
        using var state = new NetworkCacheSecretState();

        Assert.Throws<InvalidDataException>(() => state.SetPassword("a1", new string('x', 2561)));
    }
}
