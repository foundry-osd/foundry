// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Telemetry;

namespace Foundry.Telemetry.Tests;

public sealed class TelemetryValueNormalizerTests
{
    [Theory]
    [InlineData(TelemetryEvents.OsdBootMediaFinished, "boot_media_failure_kind", "cancellation", "cancelled")]
    [InlineData(TelemetryEvents.OsdBootMediaFinished, "boot_media_failure_kind", "cancelled", "cancelled")]
    [InlineData(TelemetryEvents.OsdBootMediaFinished, "boot_media_failure_kind", "tooling", "tooling")]
    [InlineData(TelemetryEvents.OsdBootMediaFinished, "boot_media_failure_reason", "nonzero_exit", "non_zero_exit")]
    [InlineData(TelemetryEvents.OsdBootMediaFinished, "boot_media_failure_reason", "non_zero_exit", "non_zero_exit")]
    [InlineData(TelemetryEvents.DeploySessionFinished, "deploy_session_failure_reason", "caller_cancelled", "cancelled")]
    [InlineData(TelemetryEvents.DeploySessionFinished, "deploy_session_failure_reason", "cancelled", "cancelled")]
    [InlineData(TelemetryEvents.OsdBootMediaFinished, "boot_media_failure_code", "WINPE_BUILD_FAILED", "winpe_build_failed")]
    [InlineData(TelemetryEvents.OsdBootMediaFinished, "boot_media_failure_code", "winpe_build_failed", "winpe_build_failed")]
    [InlineData(TelemetryEvents.DeploySessionFinished, "deploy_session_failure_code", "Download_Failed", "download_failed")]
    [InlineData(TelemetryEvents.DeploySessionFinished, "deploy_session_failure_code", "0x80070005", "0x80070005")]
    public void Normalize_MapsFailureVocabularyToCanonicalValues(string eventName, string key, string value, string expected)
    {
        Assert.Equal(expected, TelemetryValueNormalizer.Normalize(eventName, key, value));
    }

    [Theory]
    [InlineData("dism.exe", "dism")]
    [InlineData(" DISM.EXE ", "dism")]
    [InlineData("dism", "dism")]
    [InlineData("7-Zip", "7zip")]
    [InlineData("7zip", "7zip")]
    [InlineData("Windows ADK", "adk")]
    [InlineData("adk", "adk")]
    [InlineData("copype/MakeWinPEMedia", "makewinpemedia")]
    [InlineData("MakeWinPEMedia.exe", "makewinpemedia")]
    [InlineData("C:\\Tools\\dism.exe", "other")]
    [InlineData("a tool with spaces", "other")]
    [InlineData("averyveryveryveryverylongtoolname1", "other")]
    [InlineData("", "other")]
    public void Normalize_CanonicalizesFailureToolNames(string value, string expected)
    {
        Assert.Equal(expected, TelemetryValueNormalizer.Normalize(TelemetryEvents.OsdBootMediaFinished, "boot_media_failure_tool", value));
    }

    [Fact]
    public void Normalize_LeavesUnrelatedKeysUnchanged()
    {
        Assert.Equal("Cancellation", TelemetryValueNormalizer.Normalize(TelemetryEvents.OsdBootMediaFinished, "boot_media_target", "Cancellation"));
        Assert.Equal(42, TelemetryValueNormalizer.Normalize(TelemetryEvents.OsdBootMediaFinished, "boot_media_failure_exit_code", 42));
        Assert.Null(TelemetryValueNormalizer.Normalize(TelemetryEvents.OsdBootMediaFinished, "boot_media_failure_kind", null));
    }

    [Fact]
    public void Sanitize_StoresNormalizedValues()
    {
        IReadOnlyDictionary<string, object?> result = TelemetryEventPropertyPolicy.Sanitize(
            TelemetryEvents.OsdBootMediaFinished,
            new Dictionary<string, object?>
            {
                ["boot_media_failure_kind"] = "cancellation",
                ["boot_media_failure_code"] = "WINPE_BUILD_FAILED",
                ["boot_media_failure_tool"] = "dism.exe"
            });

        Assert.Equal("cancelled", result["boot_media_failure_kind"]);
        Assert.Equal("winpe_build_failed", result["boot_media_failure_code"]);
        Assert.Equal("dism", result["boot_media_failure_tool"]);
    }

    [Theory]
    [InlineData(TelemetryEvents.DeploySessionFinished, "deploy_driver_pack_selection_kind", "oemcatalog", "oem_catalog")]
    [InlineData(TelemetryEvents.DeploySessionFinished, "deploy_driver_pack_selection_kind", "microsoftupdatecatalog", "microsoft_update_catalog")]
    [InlineData(TelemetryEvents.DeploySessionFinished, "deploy_driver_pack_selection_kind", "oem_catalog", "oem_catalog")]
    [InlineData(TelemetryEvents.DeploySessionFinished, "deploy_driver_pack_selection_kind", "none", "none")]
    [InlineData(TelemetryEvents.DeploySessionFinished, "deploy_autopilot_hash_upload_state", "notplanned", "not_planned")]
    [InlineData(TelemetryEvents.DeploySessionFinished, "deploy_autopilot_hash_upload_state", "dryrunprepared", "dry_run_prepared")]
    [InlineData(TelemetryEvents.DeploySessionFinished, "deploy_autopilot_hash_upload_state", "skippedcertificateexpired", "skipped_certificate_expired")]
    [InlineData(TelemetryEvents.DeploySessionFinished, "deploy_autopilot_hash_upload_state", "skippedmissingconfiguration", "skipped_missing_configuration")]
    [InlineData(TelemetryEvents.DeploySessionFinished, "deploy_autopilot_hash_upload_state", "capturefailed", "capture_failed")]
    [InlineData(TelemetryEvents.DeploySessionFinished, "deploy_autopilot_hash_upload_state", "uploadfailed", "upload_failed")]
    [InlineData(TelemetryEvents.DeploySessionFinished, "deploy_autopilot_hash_upload_state", "uploadtimedout", "upload_timed_out")]
    [InlineData(TelemetryEvents.DeploySessionFinished, "deploy_autopilot_hash_upload_state", "not_planned", "not_planned")]
    [InlineData(TelemetryEvents.DeploySessionFinished, "deploy_autopilot_hash_upload_state", "completed", "completed")]
    [InlineData(TelemetryEvents.OsdBootMediaFinished, "boot_media_boot_image_source", "winrewifi", "winre_wifi")]
    [InlineData(TelemetryEvents.OsdBootMediaFinished, "boot_media_boot_image_source", "winre_wifi", "winre_wifi")]
    [InlineData(TelemetryEvents.OsdBootMediaFinished, "boot_media_boot_image_source", "winpe", "winpe")]
    [InlineData(TelemetryEvents.BootstrapFailed, "last_stage", "deploymentpreparation", "deployment_preparation")]
    [InlineData(TelemetryEvents.BootstrapFailed, "last_stage", "deployment_preparation", "deployment_preparation")]
    [InlineData(TelemetryEvents.BootstrapFailed, "last_stage", "connect", "connect")]
    public void Normalize_SendsSnakeCaseEnumValues(string eventName, string key, string value, string expected)
    {
        Assert.Equal(expected, TelemetryValueNormalizer.Normalize(eventName, key, value));
    }

    [Theory]
    [InlineData("Pro", "professional")]
    [InlineData("pro", "professional")]
    [InlineData("professional", "professional")]
    [InlineData("Pro N", "professionaln")]
    [InlineData("Pro Education", "professionaleducation")]
    [InlineData("Pro Education N", "professionaleducationn")]
    [InlineData("Pro for Workstations", "professionalworkstation")]
    [InlineData("Pro for Workstations N", "professionalworkstationn")]
    [InlineData("Home", "core")]
    [InlineData("Home N", "coren")]
    [InlineData("Home Single Language", "coresinglelanguage")]
    [InlineData("enterprise n", "enterprisen")]
    [InlineData("Enterprise-N", "enterprisen")]
    [InlineData("ProfessionalStudent", "professionalstudent")]
    [InlineData("CoreConnectedSingleLanguage", "coreconnectedsinglelanguage")]
    [InlineData("My custom edition", "unknown")]
    [InlineData("unknown", "unknown")]
    public void Normalize_CanonicalizesWindowsEditions(string value, string expected)
    {
        Assert.Equal(expected, TelemetryValueNormalizer.Normalize(TelemetryEvents.DeploySessionFinished, "deploy_os_edition", value));
    }

    [Theory]
    [InlineData("catalog")]
    [InlineData("custom")]
    public void Sanitize_ForDeploySessionFinished_SendsCanonicalEditionForEverySource(string source)
    {
        IReadOnlyDictionary<string, object?> result = TelemetryEventPropertyPolicy.Sanitize(
            TelemetryEvents.DeploySessionFinished,
            new Dictionary<string, object?> { ["deploy_os_source"] = source, ["deploy_os_edition"] = "Pro" });

        Assert.Equal("professional", result["deploy_os_edition"]);
    }

    [Fact]
    public void Sanitize_ForBootstrapFailed_KeepsLegacyStageSpellingAsCanonicalValue()
    {
        IReadOnlyDictionary<string, object?> result = TelemetryEventPropertyPolicy.Sanitize(
            TelemetryEvents.BootstrapFailed,
            new Dictionary<string, object?> { ["last_stage"] = "deploymentpreparation" });

        Assert.Equal("deployment_preparation", result["last_stage"]);
    }
}
