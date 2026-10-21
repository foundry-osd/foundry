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
}
