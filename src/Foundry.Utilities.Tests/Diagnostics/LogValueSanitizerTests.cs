// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Utilities.Diagnostics;

namespace Foundry.Utilities.Tests.Diagnostics;

public sealed class LogValueSanitizerTests
{
    [Fact]
    public void NormalizePropertyValue_ReplacesControlCharacters()
    {
        string result = LogValueSanitizer.NormalizePropertyValue("first\r\nsecond\tthird\0");

        Assert.Equal("first second third", result);
    }

    [Fact]
    public void SanitizeUri_RemovesQueryAndFragment()
    {
        string result = LogValueSanitizer.SanitizeUri(
            new Uri("https://example.test/probe/status?token=secret#details"));

        Assert.Equal("https://example.test/probe/status", result);
    }

    [Fact]
    public void SanitizeUri_RemovesUserInformation()
    {
        string result = LogValueSanitizer.SanitizeUri(
            new Uri("https://user:password@example.test:8443/probe/status?token=secret"));

        Assert.Equal("https://example.test:8443/probe/status", result);
        Assert.DoesNotContain("user", result, StringComparison.Ordinal);
        Assert.DoesNotContain("password", result, StringComparison.Ordinal);
    }
}
