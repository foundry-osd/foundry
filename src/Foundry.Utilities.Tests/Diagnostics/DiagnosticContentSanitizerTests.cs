// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Utilities.Diagnostics;

namespace Foundry.Utilities.Tests.Diagnostics;

public sealed class DiagnosticContentSanitizerTests
{
    [Fact]
    public void Sanitize_RemovesSensitiveDiagnosticValues()
    {
        string actual = DiagnosticContentSanitizer.Sanitize(
            "Authorization=Bearer secret Token=hidden https://example.test/a?token=secret#fragment C:\\Users\\alice\\file.txt alice@example.test");

        Assert.DoesNotContain("secret", actual, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hidden", actual, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alice", actual, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("?token=", actual, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fragment", actual, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("https://example.test/a", actual, StringComparison.Ordinal);
    }

    [Fact]
    public void Sanitize_RemovesIdentifiersAndControlCharacters()
    {
        string actual = DiagnosticContentSanitizer.Sanitize(
            "DeviceId=8a8098ce-cf30-4db3-a602-f09c770abca1\r\nTarget computer name configured: DEVICE-123\tcomplete");

        Assert.DoesNotContain("8a8098ce", actual, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DEVICE-123", actual, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain('\r', actual);
        Assert.DoesNotContain('\n', actual);
        Assert.DoesNotContain('\t', actual);
    }

    [Fact]
    public void Sanitize_RedactsAdditionalUriSchemesAndCredentialLabels()
    {
        string actual = DiagnosticContentSanitizer.Sanitize(
            "file://server/share/boot.wim ms-appx:///Assets/Secrets.json custom+tool://tenant/resource AccessToken=secret RefreshToken=\"refresh\" ClientSecret: client-value");

        Assert.DoesNotContain("file://", actual, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ms-appx://", actual, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("custom+tool://", actual, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AccessToken=secret", actual, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RefreshToken=\"refresh\"", actual, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("client-value", actual, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<redacted:uri>", actual, StringComparison.Ordinal);
        Assert.Contains("AccessToken=<redacted>", actual, StringComparison.Ordinal);
        Assert.Contains("RefreshToken=<redacted>", actual, StringComparison.Ordinal);
        Assert.Contains("ClientSecret=<redacted>", actual, StringComparison.Ordinal);
    }

    [Fact]
    public void Sanitize_TruncatesLongValuesDeterministically()
    {
        string actual = DiagnosticContentSanitizer.Sanitize(new string('a', 50), maximumLength: 20);

        Assert.Equal("aaaaaaaaa<truncated>", actual);
        Assert.Equal(20, actual.Length);
    }

    [Fact]
    public void Sanitize_PreservesOrdinaryDiagnosticText()
    {
        string actual = DiagnosticContentSanitizer.Sanitize("DISM failed with exit code 87.");

        Assert.Equal("DISM failed with exit code 87.", actual);
    }
}
