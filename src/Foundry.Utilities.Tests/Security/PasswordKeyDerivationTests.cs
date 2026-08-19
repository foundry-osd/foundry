// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Utilities.Security;

namespace Foundry.Utilities.Tests.Security;

public sealed class PasswordKeyDerivationTests
{
    [Fact]
    public void DeriveKey_WithSameInputs_ReturnsSameKey()
    {
        byte[] salt = Enumerable.Range(0, 16).Select(value => (byte)value).ToArray();

        byte[] first = PasswordKeyDerivation.DeriveKey("correct horse".AsSpan(), salt, 600_000, 32);
        byte[] second = PasswordKeyDerivation.DeriveKey("correct horse".AsSpan(), salt, 600_000, 32);

        Assert.Equal(first, second);
        Assert.Equal(32, first.Length);
    }

    [Fact]
    public void GenerateSalt_ReturnsRandom16ByteValues()
    {
        byte[] first = PasswordKeyDerivation.GenerateSalt();
        byte[] second = PasswordKeyDerivation.GenerateSalt();

        Assert.Equal(16, first.Length);
        Assert.Equal(16, second.Length);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void DeriveKey_WithZeroIterations_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PasswordKeyDerivation.DeriveKey("password".AsSpan(), new byte[16], 0, 32));
    }

    [Fact]
    public void DeriveKey_WithNon16ByteSalt_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            PasswordKeyDerivation.DeriveKey("password".AsSpan(), new byte[15], 600_000, 32));
    }
}
