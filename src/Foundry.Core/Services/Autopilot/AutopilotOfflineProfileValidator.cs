// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Foundry.Core.Services.Autopilot;

/// <summary>Validates offline user-driven profile semantics without rewriting valid manual JSON.</summary>
public static class AutopilotOfflineProfileValidator
{
    public const int MaximumContentLength = 1024 * 1024;
    private const int SupportedOobeFlags = 1 | 2 | 4 | 8 | 16 | 256 | 1024;

    /// <summary>Rejects non-ASCII file bytes, including encoding markers, without lossy decoding.</summary>
    public static void Validate(ReadOnlySpan<byte> content)
    {
        if (content.Length > MaximumContentLength) throw Invalid("The offline Autopilot profile exceeds its size limit.");
        foreach (byte value in content)
            if (value > 127) throw Invalid("The offline Autopilot profile must use ASCII file bytes without an encoding marker.");
        Validate(Encoding.ASCII.GetString(content));
    }

    public static void Validate(string jsonContent)
    {
        if (string.IsNullOrWhiteSpace(jsonContent) || jsonContent.Length > MaximumContentLength || jsonContent.Any(character => character > 127))
            throw Invalid("The offline Autopilot profile must contain bounded ASCII JSON.");
        try
        {
            using JsonDocument document = JsonDocument.Parse(jsonContent, new JsonDocumentOptions { MaxDepth = 32 });
            JsonElement root = document.RootElement;
            RequireObject(root);
            Guid tenant = RequireGuid(root, "CloudAssignedTenantId");
            RequireGuid(root, "ZtdCorrelationId");
            string domain = RequireString(root, "CloudAssignedTenantDomain");
            ValidateDomain(domain);
            int join = RequireNumber(root, "CloudAssignedDomainJoinMethod");
            int forced = RequireNumber(root, "CloudAssignedForcedEnrollment");
            int flags = RequireNumber(root, "CloudAssignedOobeConfig");
            if (join is not (0 or 1)) throw Invalid("CloudAssignedDomainJoinMethod must select user-driven Entra or hybrid join.");
            if (forced is not (0 or 1)) throw Invalid("CloudAssignedForcedEnrollment must be 0 or 1.");
            if (flags < 0 || (flags & ~SupportedOobeFlags) != 0)
                throw Invalid("CloudAssignedOobeConfig contains unsupported offline deployment flags; self-deploying and pre-provisioning require an assigned online profile.");
            if (root.TryGetProperty("Version", out _) && RequireNumber(root, "Version") != 2049)
                throw Invalid("The offline Autopilot profile version is unsupported.");
            ValidateBranding(RequireString(root, "CloudAssignedAadServerData"), tenant, domain, forced);
            ValidateOptionalSettings(root, join);
        }
        catch (JsonException)
        {
            throw Invalid("The offline Autopilot profile or its branding data is not a valid single JSON object.");
        }
    }

    private static void ValidateBranding(string content, Guid tenant, string domain, int forced)
    {
        using JsonDocument document = JsonDocument.Parse(content, new JsonDocumentOptions { MaxDepth = 16 });
        RequireObject(document.RootElement);
        if (!document.RootElement.TryGetProperty("ZeroTouchConfig", out JsonElement branding)) throw Invalid("CloudAssignedAadServerData requires ZeroTouchConfig.");
        RequireObject(branding);
        if (!RequireString(branding, "CloudAssignedTenantDomain").Equals(domain, StringComparison.OrdinalIgnoreCase))
            throw Invalid("Branding tenant domain does not match CloudAssignedTenantDomain.");
        RequireString(branding, "CloudAssignedTenantUpn", allowEmpty: true);
        if (branding.TryGetProperty("ForcedEnrollment", out _) && RequireNumber(branding, "ForcedEnrollment") != forced)
            throw Invalid("Branding ForcedEnrollment does not match CloudAssignedForcedEnrollment.");
        if (branding.TryGetProperty("CloudAssignedTenantId", out _) && RequireGuid(branding, "CloudAssignedTenantId") != tenant)
            throw Invalid("Branding tenant ID does not match CloudAssignedTenantId.");
    }

    private static void ValidateOptionalSettings(JsonElement root, int join)
    {
        if (root.TryGetProperty("CloudAssignedDeviceName", out _))
        {
            string name = RequireString(root, "CloudAssignedDeviceName");
            if (name.Length > 63 || !Regex.IsMatch(name, "^(?:[A-Za-z0-9-]|%SERIAL%|%RAND:(?:[1-9]|1[0-5])%)+$", RegexOptions.CultureInvariant))
                throw Invalid("CloudAssignedDeviceName contains an unsupported device name or naming template.");
        }
        foreach (string property in new[] { "CloudAssignedLanguage", "CloudAssignedRegion" })
        {
            if (root.TryGetProperty(property, out _) && !CultureInfo.GetCultures(CultureTypes.SpecificCultures).Any(culture =>
                    culture.Name.Equals(RequireString(root, property), StringComparison.OrdinalIgnoreCase)))
                throw Invalid(property + " must identify a supported specific locale.");
        }
        if (root.TryGetProperty("HybridJoinSkipDCConnectivityCheck", out _) &&
            (join != 1 || RequireNumber(root, "HybridJoinSkipDCConnectivityCheck") is not (0 or 1)))
            throw Invalid("HybridJoinSkipDCConnectivityCheck requires hybrid join and a numeric 0 or 1.");
        if (root.TryGetProperty("CloudAssignedAutopilotUpdateDisabled", out _) && RequireNumber(root, "CloudAssignedAutopilotUpdateDisabled") is not (0 or 1))
            throw Invalid("CloudAssignedAutopilotUpdateDisabled must be 0 or 1.");
        if (root.TryGetProperty("CloudAssignedAutopilotUpdateTimeout", out _) && RequireNumber(root, "CloudAssignedAutopilotUpdateTimeout") <= 0)
            throw Invalid("CloudAssignedAutopilotUpdateTimeout must be a positive millisecond value.");
    }

    internal static void ValidateDomain(string domain)
    {
        if (domain.Length > 253 || domain != domain.Trim() || !domain.Contains('.') || Uri.CheckHostName(domain) != UriHostNameType.Dns)
            throw Invalid("CloudAssignedTenantDomain must be a DNS tenant domain.");
    }

    private static void RequireObject(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Invalid("An offline Autopilot profile and its branding data must be single objects.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonProperty property in value.EnumerateObject())
            if (!names.Add(property.Name)) throw Invalid("Offline Autopilot JSON contains an ambiguous duplicate property.");
    }

    private static string RequireString(JsonElement root, string property, bool allowEmpty = false)
    {
        if (!root.TryGetProperty(property, out JsonElement value) || value.ValueKind != JsonValueKind.String ||
            !allowEmpty && string.IsNullOrWhiteSpace(value.GetString())) throw Invalid(property + " must be a string with the required value.");
        return value.GetString()!;
    }

    private static int RequireNumber(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out JsonElement value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int number))
            throw Invalid(property + " must be a supported integer.");
        return number;
    }

    private static Guid RequireGuid(JsonElement root, string property)
    {
        if (!Guid.TryParseExact(RequireString(root, property), "D", out Guid value) || value == Guid.Empty)
            throw Invalid(property + " must be a nonempty GUID without braces.");
        return value;
    }

    private static InvalidDataException Invalid(string message) => new(message);
}
