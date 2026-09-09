// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.RegularExpressions;
using Foundry.Utilities.Diagnostics;

namespace Foundry.Telemetry;

/// <summary>
/// Redacts remote technical text before truncation so retained tails cannot expose part of a secret.
/// </summary>
internal static partial class RemoteDiagnosticText
{
    private const int MaximumOutputLength = 8192;
    private const string Truncated = "<truncated>";

    internal static string Sanitize(string? text, int maximumLength) =>
        DiagnosticContentSanitizer.Sanitize(Redact(text), maximumLength);

    internal static string SanitizeOutput(string text)
    {
        string sanitized = DiagnosticContentSanitizer.SanitizeMultiline(Redact(text), int.MaxValue);
        if (sanitized.Length <= MaximumOutputLength)
        {
            return sanitized;
        }

        return Truncated + "\n" + sanitized[^(MaximumOutputLength - Truncated.Length - 1)..];
    }

    private static string Redact(string? text)
    {
        string value = UriPattern().Replace(text ?? string.Empty, "<redacted:uri>");
        value = QuotedPathPattern().Replace(value, "<redacted:path>");
        value = WindowsPathPattern().Replace(value, "<redacted:path>");
        // Tool output can contain XML/JSON configuration as well as conventional key=value text.
        value = SensitiveJsonPattern().Replace(value, "$1\"<redacted>\"");
        value = SensitiveXmlPattern().Replace(value, "$1<redacted>$2");
        value = AuthorizationPattern().Replace(value, "Authorization=<redacted>");
        return value;
    }

    [GeneratedRegex("\\b[A-Za-z][A-Za-z0-9+.-]*://[^\\s\\\"'<>]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UriPattern();

    [GeneratedRegex("[\"'](?:[A-Za-z]:\\\\|\\\\\\\\)[^\"'\\r\\n]*[\"']", RegexOptions.CultureInvariant)]
    private static partial Regex QuotedPathPattern();

    // An unquoted path has no universal terminator. Recognize common diagnostic separators
    // while retaining the conservative whole-path fallback for names containing spaces.
    [GeneratedRegex("(?<![A-Za-z0-9])(?:[A-Za-z]:\\\\|\\\\\\\\)[^\\r\\n\\\"'<>|]*?(?=\\s+(?:failed|returned|because|was|is|could|with)\\b|[,:;]\\s|[\\r\\n\\\"'<>|]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WindowsPathPattern();

    [GeneratedRegex("(\"(?:password|passphrase|secret|clientSecret|accessToken|refreshToken|token|apiKey|authorization|serialNumber|hardwareHash|computerName|ssid)\"\\s*:\\s*)\"(?:\\\\.|[^\"\\\\])*\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveJsonPattern();

    [GeneratedRegex("(<(?:Password|Passphrase|Secret|ClientSecret|AccessToken|RefreshToken|Token|KeyMaterial|HardwareHash|ComputerName)(?:\\s[^>]*)?>)[\\s\\S]*?(</(?:Password|Passphrase|Secret|ClientSecret|AccessToken|RefreshToken|Token|KeyMaterial|HardwareHash|ComputerName)>)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveXmlPattern();

    [GeneratedRegex("\\bAuthorization\\s*[:=]\\s*(?:Basic|Bearer|Negotiate|NTLM)\\s+[^\\s,;\"<>]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AuthorizationPattern();
}
