// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Utilities.Security;

namespace Foundry.Utilities.Tests.Security;

public sealed class Base64UrlTests
{
    [Fact]
    public void EncodeDecode_RoundTripsWithoutPaddingCharacters()
    {
        byte[] value = [0xfb, 0xff, 0x00, 0x01];

        string encoded = Base64Url.Encode(value);

        Assert.DoesNotContain("+", encoded, StringComparison.Ordinal);
        Assert.DoesNotContain("/", encoded, StringComparison.Ordinal);
        Assert.DoesNotContain("=", encoded, StringComparison.Ordinal);
        Assert.Equal(value, Base64Url.Decode(encoded));
    }

    [Theory]
    [InlineData("not+url")]
    [InlineData("not/url")]
    [InlineData("padded=")]
    [InlineData("a")]
    public void Decode_WithInvalidValue_ThrowsFormatException(string value)
    {
        Assert.Throws<FormatException>(() => Base64Url.Decode(value));
    }
}
