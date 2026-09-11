// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Utilities.Diagnostics;
using Serilog.Events;
using Serilog.Parsing;

namespace Foundry.Utilities.Tests.Diagnostics;

public sealed class LogEventNormalizerTests
{
    [Fact]
    public void Normalize_MasksSignedUrlsUsedAsDictionaryKeysWithoutDroppingCollidingEntries()
    {
        var source = new LogEvent(DateTimeOffset.UtcNow, LogEventLevel.Debug, null,
            new MessageTemplateParser().Parse("Results {@Results}"), [new("Results", new DictionaryValue([
                new(new ScalarValue("https://example.test?sig=first-secret"), new ScalarValue(42)),
                new(new ScalarValue("https://example.test?sig=second-secret"), new ScalarValue(43))]))]);

        LogEvent result = LogEventNormalizer.Normalize(source);

        Assert.DoesNotContain("first-secret", result.RenderMessage(), StringComparison.Ordinal);
        Assert.DoesNotContain("second-secret", result.RenderMessage(), StringComparison.Ordinal);
        DictionaryValue values = Assert.IsType<DictionaryValue>(result.Properties["Results"]);
        Assert.Equal(2, values.Elements.Count);
        Assert.Contains(values.Elements.Values, value => Equals(((ScalarValue)value).Value, 42));
        Assert.Contains(values.Elements.Values, value => Equals(((ScalarValue)value).Value, 43));
    }

    [Theory]
    [InlineData("Password: {Value}")]
    [InlineData("Running tool --password {Value}")]
    [InlineData("URL https://example.test?sig={Value}")]
    public void Normalize_RecognizesCredentialsFromTemplateEvenWhenPropertyNameIsGeneric(string template)
    {
        var source = new LogEvent(DateTimeOffset.UtcNow, LogEventLevel.Debug, null,
            new MessageTemplateParser().Parse(template), [new("Value", new ScalarValue("never-export"))]);

        LogEvent result = LogEventNormalizer.Normalize(source);

        Assert.DoesNotContain("never-export", result.RenderMessage(), StringComparison.Ordinal);
        Assert.Equal("<redacted>", ((ScalarValue)result.Properties["Value"]).Value);
    }

    [Fact]
    public void Normalize_PreservesExceptionIdentityForIndependentErrorTrackingDeduplication()
    {
        var exception = new InvalidOperationException("password=never-export");
        var source = new LogEvent(DateTimeOffset.UtcNow, LogEventLevel.Error, exception,
            new MessageTemplateParser().Parse("Failed"), []);

        LogEvent first = LogEventNormalizer.Normalize(source);
        LogEvent second = LogEventNormalizer.Normalize(source);

        Assert.Same(first.Exception, second.Exception);
        Assert.DoesNotContain("never-export", first.Exception!.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--password \"never-export\"", "never-export")]
    [InlineData("Cookie: session=never-export; other=also-private", "never-export")]
    [InlineData("Cookie: session=never-export; other=also-private", "also-private")]
    [InlineData("-----BEGIN RSA PRIVATE KEY-----\nnever-export\n-----END RSA PRIVATE KEY-----", "never-export")]
    [InlineData("https://example.test/file?X-Amz-Signature=never-export&version=42", "never-export")]
    [InlineData("https://user:never-export@example.test/file", "never-export")]
    public void Normalize_MasksEmbeddedAuthenticationSecrets(string text, string secret)
    {
        var source = new LogEvent(DateTimeOffset.UtcNow, LogEventLevel.Debug, null,
            new MessageTemplateParser().Parse("Output: {Output}"), [new("Output", new ScalarValue(text))]);

        LogEvent result = LogEventNormalizer.Normalize(source);

        Assert.DoesNotContain(secret, result.RenderMessage(), StringComparison.Ordinal);
        Assert.Contains("<redacted>", result.RenderMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void Normalize_MasksCredentialNamesInsideDictionariesAndPreservesNonSecrets()
    {
        var source = new LogEvent(DateTimeOffset.UtcNow, LogEventLevel.Information, null,
            new MessageTemplateParser().Parse("Settings {@Settings}"),
            [new("Settings", new DictionaryValue([
                new(new ScalarValue("NetworkPassword"), new ScalarValue("never-export")),
                new(new ScalarValue("TokenCount"), new ScalarValue(42)),
                new(new ScalarValue("TenantId"), new ScalarValue("tenant-42"))]))]);

        LogEvent result = LogEventNormalizer.Normalize(source);

        Assert.DoesNotContain("never-export", result.RenderMessage(), StringComparison.Ordinal);
        Assert.Contains("tenant-42", result.RenderMessage(), StringComparison.Ordinal);
        Assert.Contains("42", result.RenderMessage(), StringComparison.Ordinal);
    }
}
