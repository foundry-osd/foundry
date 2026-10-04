// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text;
using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Configuration;

/// <summary>Parses directory names structurally without rewriting their source representation.</summary>
public static class DistinguishedNameRules
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>Accepts escaped LDAP DNs, including UTF-8 hex escapes; LDAP filter escaping is a separate operation.</summary>
    public static bool TryParse(string value, out ParsedDistinguishedName parsed)
    {
        parsed = new([], null);
        if (string.IsNullOrWhiteSpace(value) || value.Length > 4096 || value.Contains('\0')) return false;
        if (!TrySplit(value, ',', out List<string> rawRdns)) return false;
        var rdns = new List<DistinguishedNameRdn>();
        foreach (string rawRdn in rawRdns)
        {
            if (!TrySplit(rawRdn, '+', out List<string> rawAttributes)) return false;
            var attributes = new List<DistinguishedNameAttribute>();
            foreach (string rawAttribute in rawAttributes)
            {
                int equals = rawAttribute.IndexOf('=');
                if (equals <= 0) return false;
                string type = rawAttribute[..equals].Trim();
                if (!IsAttributeType(type) || !TryDecode(rawAttribute[(equals + 1)..], out string decoded)) return false;
                attributes.Add(new(type, decoded));
            }

            rdns.Add(new(attributes));
        }

        parsed = new(rdns, rawRdns.Count > 1 ? value[(rawRdns[0].Length + 1)..] : null);
        return true;
    }

    /// <summary>Matches single-valued DC suffixes structurally; escaped delimiters cannot introduce a domain suffix.</summary>
    public static bool IsWithinDomain(string distinguishedName, string dnsDomain)
    {
        if (!DomainJoinCredentialContext.IsValidDomainName(dnsDomain) || !TryParse(distinguishedName, out ParsedDistinguishedName parsed)) return false;
        string[] labels = DomainJoinCredentialContext.CanonicalizeDomainName(dnsDomain).Split('.');
        if (parsed.Rdns.Count < labels.Length) return false;
        int domainStart = parsed.Rdns.Count - labels.Length;
        if (domainStart > 0 && parsed.Rdns[domainStart - 1].Attributes.Any(attribute =>
            string.Equals(attribute.Type, "DC", StringComparison.OrdinalIgnoreCase))) return false;
        for (int index = 0; index < labels.Length; index++)
        {
            DistinguishedNameRdn rdn = parsed.Rdns[domainStart + index];
            if (rdn.Attributes.Count != 1 || !string.Equals(rdn.Attributes[0].Type, "DC", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(rdn.Attributes[0].Value, labels[index], StringComparison.OrdinalIgnoreCase)) return false;
        }

        return true;
    }

    /// <summary>Detects duplicate authored destinations only; live directory GUIDs remain authoritative.</summary>
    internal static string GetComparisonKey(ParsedDistinguishedName parsed) => string.Join("|", parsed.Rdns.Select(rdn =>
        string.Join("+", rdn.Attributes.Select(attribute => $"{attribute.Type.ToUpperInvariant()}:{Convert.ToHexString(Encoding.UTF8.GetBytes(attribute.Value.ToUpperInvariant()))}").Order(StringComparer.Ordinal))));

    private static bool TrySplit(string value, char separator, out List<string> parts)
    {
        parts = [];
        int start = 0;
        for (int index = 0; index < value.Length; index++)
        {
            if (value[index] == '\\')
            {
                if (++index >= value.Length) return false;
                continue;
            }

            if (value[index] != separator) continue;
            if (index == start) return false;
            parts.Add(value[start..index]);
            start = index + 1;
        }

        if (start == value.Length) return false;
        parts.Add(value[start..]);
        return true;
    }

    private static bool IsAttributeType(string type) => type.Length > 0 &&
        (char.IsAsciiLetter(type[0]) && type.All(character => char.IsAsciiLetterOrDigit(character) || character == '-') ||
         char.IsAsciiDigit(type[0]) && type.All(character => char.IsAsciiDigit(character) || character == '.') &&
         type.Split('.').All(part => part.Length > 0));

    private static bool TryDecode(string raw, out string decoded)
    {
        decoded = string.Empty;
        if (raw.Length == 0 || raw[0] is ' ' or '#' || raw[^1] == ' ' && !HasEscapedLastSpace(raw)) return false;
        var builder = new StringBuilder();
        for (int index = 0; index < raw.Length; index++)
        {
            char character = raw[index];
            if (character != '\\')
            {
                if (character is '"' or '+' or ',' or ';' or '<' or '>' || char.IsControl(character)) return false;
                builder.Append(character);
                continue;
            }

            if (++index >= raw.Length) return false;
            if (index + 1 < raw.Length && Uri.IsHexDigit(raw[index]) && Uri.IsHexDigit(raw[index + 1]))
            {
                var bytes = new List<byte>();
                while (true)
                {
                    bytes.Add(Convert.ToByte(raw.Substring(index, 2), 16));
                    index++;
                    if (index + 3 >= raw.Length || raw[index + 1] != '\\' || !Uri.IsHexDigit(raw[index + 2]) || !Uri.IsHexDigit(raw[index + 3])) break;
                    index += 2;
                }

                try { builder.Append(StrictUtf8.GetString(bytes.ToArray())); }
                catch (DecoderFallbackException) { return false; }
            }
            else
            {
                if (raw[index] is not (' ' or '#' or '"' or '+' or ',' or ';' or '<' or '>' or '=' or '\\')) return false;
                builder.Append(raw[index]);
            }
        }

        decoded = builder.ToString();
        try { StrictUtf8.GetByteCount(decoded); }
        catch (EncoderFallbackException) { return false; }
        return decoded.Length > 0 && !decoded.Contains('\0');
    }

    private static bool HasEscapedLastSpace(string raw)
    {
        int slashes = 0;
        for (int index = raw.Length - 2; index >= 0 && raw[index] == '\\'; index--) slashes++;
        return slashes % 2 == 1;
    }
}

/// <summary>Contains leaf-first RDNs and the original escaped parent name.</summary>
public sealed record ParsedDistinguishedName(IReadOnlyList<DistinguishedNameRdn> Rdns, string? Parent);

/// <summary>Represents one possibly multivalued relative distinguished name.</summary>
public sealed record DistinguishedNameRdn(IReadOnlyList<DistinguishedNameAttribute> Attributes);

/// <summary>Contains an attribute type and decoded value for structural comparison only.</summary>
public sealed record DistinguishedNameAttribute(string Type, string Value);
