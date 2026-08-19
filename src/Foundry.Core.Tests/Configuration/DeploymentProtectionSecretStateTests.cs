// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Configuration;

namespace Foundry.Core.Tests.Configuration;

public sealed class DeploymentProtectionSecretStateTests
{
    [Fact]
    public void SetMatchingValues_ExposesConfirmedPasswordCopy()
    {
        using var state = new DeploymentProtectionSecretState();
        state.SetPassword("deployment passphrase".AsSpan());
        state.SetConfirmation("deployment passphrase".AsSpan());

        char[] first = state.GetConfirmedPasswordCopy();
        char[] second = state.GetConfirmedPasswordCopy();

        Assert.True(state.IsValid);
        Assert.Equal("deployment passphrase", new string(first));
        Assert.Equal(first, second);
        Assert.NotSame(first, second);
    }

    [Fact]
    public void ReplacingPassword_InvalidatesPreviousConfirmation()
    {
        using var state = new DeploymentProtectionSecretState();
        state.SetPassword("deployment passphrase".AsSpan());
        state.SetConfirmation("deployment passphrase".AsSpan());

        state.SetPassword("different passphrase".AsSpan());

        Assert.False(state.IsValid);
        Assert.Throws<InvalidOperationException>(() => state.GetConfirmedPasswordCopy());
    }

    [Fact]
    public void SetDifferentValues_ExposesIndependentCopiesForUiRestoration()
    {
        using var state = new DeploymentProtectionSecretState();
        state.SetPassword("deployment passphrase".AsSpan());
        state.SetConfirmation("different passphrase".AsSpan());

        char[] password = state.GetPasswordCopy();
        char[] confirmation = state.GetConfirmationCopy();

        Assert.Equal("deployment passphrase", new string(password));
        Assert.Equal("different passphrase", new string(confirmation));
        Assert.NotSame(password, state.GetPasswordCopy());
        Assert.NotSame(confirmation, state.GetConfirmationCopy());
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("short", "short")]
    [InlineData("deployment passphrase", "different passphrase")]
    public void InvalidPasswordState_IsNotReady(string password, string confirmation)
    {
        using var state = new DeploymentProtectionSecretState();
        state.SetPassword(password.AsSpan());
        state.SetConfirmation(confirmation.AsSpan());

        Assert.False(state.IsValid);
        Assert.Throws<InvalidOperationException>(() => state.GetConfirmedPasswordCopy());
    }

    [Fact]
    public void Clear_RemovesPasswordAndConfirmation()
    {
        using var state = new DeploymentProtectionSecretState();
        state.SetPassword("deployment passphrase".AsSpan());
        state.SetConfirmation("deployment passphrase".AsSpan());

        state.Clear();

        Assert.False(state.HasPassword);
        Assert.False(state.HasConfirmation);
        Assert.False(state.IsValid);
    }

    [Fact]
    public void Dispose_PreventsFurtherSecretAccess()
    {
        var state = new DeploymentProtectionSecretState();
        state.SetPassword("deployment passphrase".AsSpan());
        state.SetConfirmation("deployment passphrase".AsSpan());

        state.Dispose();

        Assert.Throws<ObjectDisposedException>(() => state.GetConfirmedPasswordCopy());
        Assert.Throws<ObjectDisposedException>(() => state.SetPassword("another password".AsSpan()));
    }
}
