// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration;

/// <summary>Binds a secret to its target domain and qualified account; passwords are never normalized.</summary>
public sealed record DomainJoinCredentialContext(string DomainName, string AccountName)
{
    /// <summary>Returns the canonical DNS spelling used for comparison and secret ownership.</summary>
    public static string CanonicalizeDomainName(string? value) => (value ?? string.Empty).Trim().TrimEnd('.').ToLowerInvariant();

    /// <summary>Returns the case-insensitive account spelling used for secret ownership.</summary>
    public static string CanonicalizeAccountName(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();

    /// <summary>Compares ownership without comparing or exposing a password.</summary>
    public bool Matches(DomainJoinCredentialContext other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return string.Equals(CanonicalizeDomainName(DomainName), CanonicalizeDomainName(other.DomainName), StringComparison.Ordinal) &&
            string.Equals(CanonicalizeAccountName(AccountName), CanonicalizeAccountName(other.AccountName), StringComparison.Ordinal);
    }

    /// <summary>Validates a DNS domain suitable for structural OU suffix checks.</summary>
    public static bool IsValidDomainName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 253 || value.Contains('\0')) return false;
        string domain = value.Trim();
        if (domain.EndsWith('.')) domain = domain[..^1];
        string[] labels = domain.Split('.');
        return labels.Length >= 2 && labels.All(label => label.Length is >= 1 and <= 63 &&
            char.IsAsciiLetterOrDigit(label[0]) && char.IsAsciiLetterOrDigit(label[^1]) &&
            label.All(character => char.IsAsciiLetterOrDigit(character) || character == '-'));
    }
}
