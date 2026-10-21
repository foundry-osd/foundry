// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Telemetry;

/// <summary>
/// Maps validated telemetry values to one canonical spelling per meaning, so reports do not split one value in two.
/// </summary>
/// <remarks>
/// Runs after <see cref="TelemetryEventPropertyPolicy"/> validation and only changes values, never property names.
/// Every mapping is idempotent: a canonical value is returned unchanged, so persisted records can be revalidated.
/// </remarks>
internal static class TelemetryValueNormalizer
{
    private const int MaximumToolNameLength = 32;

    /// <summary>
    /// Returns the canonical value for a property, or <paramref name="value"/> unchanged for properties it does not handle.
    /// </summary>
    /// <param name="eventName">Stable telemetry event name.</param>
    /// <param name="key">Property name.</param>
    /// <param name="value">Validated property value.</param>
    /// <returns>The canonical property value.</returns>
    internal static object? Normalize(string eventName, string key, object? value)
    {
        if (value is not string text)
        {
            return value;
        }

        return key switch
        {
            "boot_media_failure_kind" => text == "cancellation" ? "cancelled" : text,
            "boot_media_failure_reason" => text == "nonzero_exit" ? "non_zero_exit" : text,
            "deploy_session_failure_reason" => text == "caller_cancelled" ? "cancelled" : text,
            "boot_media_failure_code" or "deploy_session_failure_code" => text.ToLowerInvariant(),
            "boot_media_failure_tool" => NormalizeToolName(text),
            "deploy_driver_pack_selection_kind" => text switch
            {
                "oemcatalog" => "oem_catalog",
                "microsoftupdatecatalog" => "microsoft_update_catalog",
                _ => text
            },
            "deploy_autopilot_hash_upload_state" => text switch
            {
                "notplanned" => "not_planned",
                "dryrunprepared" => "dry_run_prepared",
                "skippedcertificateexpired" => "skipped_certificate_expired",
                "skippedmissingconfiguration" => "skipped_missing_configuration",
                "capturefailed" => "capture_failed",
                "uploadfailed" => "upload_failed",
                "uploadtimedout" => "upload_timed_out",
                _ => text
            },
            "boot_media_boot_image_source" => text == "winrewifi" ? "winre_wifi" : text,
            "last_stage" when eventName == TelemetryEvents.BootstrapFailed =>
                text == "deploymentpreparation" ? "deployment_preparation" : text,
            "deploy_os_edition" => NormalizeWindowsEdition(text),
            _ => text
        };
    }

    // Catalog and custom images name editions differently (Pro, Professional, Pro N); one canonical name is sent.
    private static string NormalizeWindowsEdition(string edition)
    {
        string normalized = new(edition.Where(character => character is not (' ' or '-')).ToArray());
        normalized = normalized.ToLowerInvariant() switch
        {
            "pro" => "professional",
            "pron" => "professionaln",
            "proeducation" => "professionaleducation",
            "proeducationn" => "professionaleducationn",
            "proforworkstations" => "professionalworkstation",
            "proforworkstationsn" => "professionalworkstationn",
            "home" => "core",
            "homen" => "coren",
            "homesinglelanguage" => "coresinglelanguage",
            string lowered => lowered
        };

        return TelemetryEventPropertyPolicy.IsKnownWindowsEdition(normalized) ? normalized : "unknown";
    }

    // Tool names come from fixed diagnostics but mix casing, extensions and display names; anything else becomes "other".
    private static string NormalizeToolName(string toolName)
    {
        string normalized = toolName.Trim().ToLowerInvariant();
        if (normalized.EndsWith(".exe", StringComparison.Ordinal))
        {
            normalized = normalized[..^4];
        }

        normalized = normalized switch
        {
            "7-zip" => "7zip",
            "windows adk" => "adk",
            "copype/makewinpemedia" => "makewinpemedia",
            _ => normalized
        };

        return normalized.Length is > 0 and <= MaximumToolNameLength && normalized.All(IsToolNameCharacter)
            ? normalized
            : "other";
    }

    private static bool IsToolNameCharacter(char character) =>
        char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character is '.' or '_' or '-';
}
