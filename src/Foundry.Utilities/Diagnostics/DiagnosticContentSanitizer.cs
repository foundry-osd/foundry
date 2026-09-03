// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.RegularExpressions;

namespace Foundry.Utilities.Diagnostics;

/// <summary>
/// Removes common credentials and direct identifiers from diagnostic text before it is shared.
/// </summary>
public static partial class DiagnosticContentSanitizer
{
    private const string TruncationMarker = "<truncated>";

    /// <summary>
    /// Normalizes and redacts diagnostic text, then bounds its final length.
    /// </summary>
    /// <param name="value">Potentially sensitive diagnostic text.</param>
    /// <param name="maximumLength">Maximum length of the returned value.</param>
    /// <returns>Redacted, single-line diagnostic text.</returns>
    public static string Sanitize(string? value, int maximumLength = 2048)
    {
        return SanitizeCore(LogValueSanitizer.NormalizePropertyValue(value), maximumLength);
    }

    /// <summary>
    /// Redacts diagnostic text while preserving its existing line and stack-trace layout.
    /// </summary>
    /// <param name="value">Potentially sensitive multiline diagnostic text.</param>
    /// <param name="maximumLength">Maximum length of the returned value.</param>
    /// <returns>Redacted diagnostic text with its line boundaries preserved.</returns>
    public static string SanitizeMultiline(string? value, int maximumLength = 65536)
    {
        return SanitizeCore(value ?? string.Empty, maximumLength);
    }

    private static string SanitizeCore(string value, int maximumLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumLength, TruncationMarker.Length);

        string sanitized = value;
        sanitized = UriPattern().Replace(sanitized, static match => SanitizeUriText(match.Value));
        sanitized = BearerTokenPattern().Replace(sanitized, "Bearer <redacted>");
        sanitized = SensitivePropertyPattern().Replace(sanitized, "$1=<redacted>");
        sanitized = TargetComputerNameMessagePattern().Replace(sanitized, "$1<redacted>");
        sanitized = WindowsUserPathPattern().Replace(sanitized, "$1<redacted>");
        sanitized = EmailAddressPattern().Replace(sanitized, "<redacted:email>");
        sanitized = GuidPattern().Replace(sanitized, "<redacted:id>");

        if (sanitized.Length <= maximumLength)
        {
            return sanitized;
        }

        return sanitized[..(maximumLength - TruncationMarker.Length)] + TruncationMarker;
    }

    private static string SanitizeUriText(string value)
    {
        string trimmed = value.TrimEnd('.', ',', ';', ')', ']', '}');
        string suffix = value[trimmed.Length..];
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri))
        {
            return "<redacted:uri>" + suffix;
        }

        return uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
               uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            ? LogValueSanitizer.SanitizeUri(uri) + suffix
            : "<redacted:uri>" + suffix;
    }

    [GeneratedRegex("\\b[A-Za-z][A-Za-z0-9+.-]*://[^\\s\\\"'<>]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UriPattern();

    [GeneratedRegex("\\bBearer\\s+[^\\s,;|}]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BearerTokenPattern();

    [GeneratedRegex("\\b(Authorization|ApiKey|AccessToken|RefreshToken|Token|Password|Passphrase|Secret|ClientSecret|PrivateKey|MediaSecretKey|TenantId|Application(?:Object)?Id|DeviceId|Serial(?:Number)?|HardwareHash|(?:Target)?ComputerName|GroupTag|Ssid|MacAddress|IpAddress)\\s*[=:]\\s*(?:\\\"[^\\\"]*\\\"|'[^']*'|[^\\s,;|}]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitivePropertyPattern();

    [GeneratedRegex("\\b(Target computer name (?:configured|resolved|selected)\\s*:\\s*)[^\\s.,;|}]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TargetComputerNameMessagePattern();

    [GeneratedRegex("\\b([A-Za-z]:\\\\Users\\\\)[^\\\\\\s]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WindowsUserPathPattern();

    [GeneratedRegex("\\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\\.[A-Z]{2,}\\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmailAddressPattern();

    [GeneratedRegex("\\b[0-9A-F]{8}-[0-9A-F]{4}-[1-5][0-9A-F]{3}-[89AB][0-9A-F]{3}-[0-9A-F]{12}\\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GuidPattern();
}
