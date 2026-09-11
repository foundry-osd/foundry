// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.RegularExpressions;

namespace Foundry.Utilities.Diagnostics;

/// <summary>Masks authentication secrets while retaining ordinary diagnostic values and paths.</summary>
public static partial class LogSecretMasker
{
    public const string Redacted = "<redacted>";

    /// <summary>Recognizes credential property names, including common HTTP and signed URL keys.</summary>
    public static bool IsSecretName(string name)
    {
        if (name.IndexOfAny([':', '/', '?', '=', '&']) >= 0)
        {
            return false;
        }
        string normalized = string.Concat(name.Where(char.IsLetterOrDigit)).ToLowerInvariant();
        return normalized.EndsWith("password", StringComparison.Ordinal) || normalized.EndsWith("token", StringComparison.Ordinal)
            || normalized.EndsWith("secret", StringComparison.Ordinal) || normalized.EndsWith("apikey", StringComparison.Ordinal)
            || normalized.EndsWith("privatekey", StringComparison.Ordinal)
            || normalized is "password" or "passwd" or "pwd" or "token" or "accesstoken" or "refreshtoken"
            or "idtoken" or "authorization" or "proxyauthorization" or "clientsecret" or "secret"
            or "apikey" or "privatekey" or "accountkey" or "sharedaccesssignature" or "sig" or "signature"
            or "xamzsignature" or "xamzcredential" or "xamzsecuritytoken" or "xgoogsignature"
            or "xgoogcredential" or "cookie" or "setcookie" or "sas" or "sastoken";
    }

    /// <summary>Removes explicit credentials embedded in free text without truncating diagnostic content.</summary>
    public static string Mask(string text)
    {
        string result = PrivateKey().Replace(text, Redacted);
        result = CookieHeader().Replace(result, "$1" + Redacted);
        result = CredentialArgument().Replace(result, "$1" + Redacted);
        result = Authorization().Replace(result, "$1" + Redacted);
        result = CredentialAssignment().Replace(result, "$1" + Redacted);
        return UrlCredentials().Replace(result, "$1" + Redacted + "@");
    }

    /// <summary>Recognizes placeholders whose surrounding template labels their value as a credential.</summary>
    internal static HashSet<string> GetSecretTemplateProperties(string template)
    {
        return CredentialPlaceholder().Matches(template).Cast<Match>()
            .Where(match => IsSecretName(match.Groups["label"].Value))
            .Select(match => match.Groups["property"].Value).ToHashSet(StringComparer.Ordinal);
    }

    [GeneratedRegex("(?i)(?:(?<label>[a-z][a-z0-9_-]*)[\\\"']?\\s*[:=]\\s*|[-/]{1,2}(?<label>[a-z][a-z0-9_-]*)\\s+)\\{[@$]?(?<property>[a-z_][a-z0-9_]*)(?:[^}]*)\\}", RegexOptions.CultureInvariant)]
    private static partial Regex CredentialPlaceholder();

    [GeneratedRegex(@"-----BEGIN (?:[A-Z0-9]+ )*PRIVATE KEY-----[\s\S]*?-----END (?:[A-Z0-9]+ )*PRIVATE KEY-----", RegexOptions.CultureInvariant)]
    private static partial Regex PrivateKey();

    [GeneratedRegex(@"(?i)(\b(?:Bearer|Basic)\s+)[A-Za-z0-9+/=_\-.~]+", RegexOptions.CultureInvariant)]
    private static partial Regex Authorization();

    [GeneratedRegex(@"(?im)(\b(?:set-cookie|cookie)\s*:\s*)[^\r\n]+", RegexOptions.CultureInvariant)]
    private static partial Regex CookieHeader();

    [GeneratedRegex("(?i)((?<![a-z0-9])[-/]{1,2}(?:password|passwd|pwd|(?:access[_-]?|refresh[_-]?|sas[_-]?)?token|client[_-]?secret|api[_-]?key)\\s+)(?:\\\"[^\\\"]*\\\"|'[^']*'|(?!\\{)[^\\s<>]+)", RegexOptions.CultureInvariant)]
    private static partial Regex CredentialArgument();

    [GeneratedRegex("(?i)((?<![a-z0-9_])(?:password|passwd|pwd|(?:access[_-]?|refresh[_-]?|id[_-]?|sas[_-]?)?token|client[_-]?secret|secret|api[_-]?key|account[_-]?key|private[_-]?key|authorization|proxy-authorization|sharedaccesssignature|sig|signature|x-amz-(?:signature|credential|security-token)|x-goog-(?:signature|credential)|cookie|set-cookie)[\\\"']?\\s*[:=]\\s*)(?:\\\"[^\\\"]*\\\"|'[^']*'|(?!\\{)[^\\s&;,\\\"'<>}]+)", RegexOptions.CultureInvariant)]
    private static partial Regex CredentialAssignment();

    [GeneratedRegex(@"(?i)(https?://)[^\s/@]+:[^\s/@]+@", RegexOptions.CultureInvariant)]
    private static partial Regex UrlCredentials();
}
