// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Serilog.Events;
using Foundry.Telemetry;

namespace Foundry.Telemetry.Tests;

public sealed class PostHogExceptionTrackerTests
{
    [Fact]
    public void Track_PreservesSanitizedAdkDiagnosticsAfterPersistence()
    {
        LogEvent source = RemoteDiagnosticsTestData.LogEvent(LogEventLevel.Error,
            "ADK operation failed. OperationKind={OperationKind}, FailureReason={FailureReason}, ExitCode={ExitCode}",
            new InvalidOperationException("private installer path C:\\Users\\alice\\setup.exe"),
            ("OperationId", "operation-1"), ("OperationKind", "AdkInstall"),
            ("FailureKind", "adk_setup"), ("FailureReason", "installer_exit_failed"),
            ("FailedOperationName", "install_adk"), ("ExitCode", -2146889721),
            ("ExitCodeHex", "0x80091007"), ("NativeErrorCode", 5),
            ("InstallerVersion", "10.1.26100.2454"), ("InstallerName", "adksetup.exe"),
            ("SetupLogId", "setup-1"), ("SetupLogPath", "C:\\Users\\alice\\setup.log"),
            ("SetupLogContent", "private installer output"));

        RemoteDiagnosticRecord record = RemoteDiagnosticPropertyPolicy.SanitizePersistedRecord(
            RemoteDiagnosticPropertyPolicy.CreateSanitizedRecord(source, RemoteDiagnosticsTestData.Context()));
        var client = new RecordingPostHogEventClient();
        new PostHogExceptionTracker(client, "install-1").Track(record);

        CapturedPostHogEvent captured = Assert.Single(client.Events);
        Assert.Equal("operation-1", captured.Properties["operation.id"]);
        Assert.Equal("AdkInstall", captured.Properties["operation.kind"]);
        Assert.Equal("adk_setup", captured.Properties["failure.kind"]);
        Assert.Equal("installer_exit_failed", captured.Properties["failure.reason"]);
        Assert.Equal("install_adk", captured.Properties["failure.operation"]);
        Assert.Equal(-2146889721, captured.Properties["process.exit_code"]);
        Assert.Equal("0x80091007", captured.Properties["process.exit_code_hex"]);
        Assert.Equal(5, captured.Properties["process.native_error_code"]);
        Assert.Equal("10.1.26100.2454", captured.Properties["installer.version"]);
        Assert.Equal("adksetup.exe", captured.Properties["installer.name"]);
        Assert.Equal("setup-1", captured.Properties["installer.log_id"]);
        Assert.Contains("installer_exit_failed", Assert.IsType<string>(captured.Properties["$exception_message"]));
        Assert.DoesNotContain("alice", captured.SerializedProperties);
        Assert.DoesNotContain("private", captured.SerializedProperties);
        Assert.DoesNotContain("SetupLogPath", captured.SerializedProperties);
        Assert.DoesNotContain("SetupLogContent", captured.SerializedProperties);
    }

    [Fact]
    public void Track_OrdersEachExceptionStackFromEntryPointToCrashSite()
    {
        var client = new RecordingPostHogEventClient();
        var tracker = new PostHogExceptionTracker(client, "install-1");
        var record = new RemoteDiagnosticRecord(
            DateTimeOffset.UtcNow,
            LogEventLevel.Error,
            "Deployment failed",
            new Dictionary<string, object>(),
            new RemoteDiagnosticException(
                "System.InvalidOperationException",
                "redacted outer",
                "   at Foundry.Deploy.Validate() in <redacted:path>:line 30\r\n" +
                "   at Foundry.Deploy.Run() in <redacted:path>:line 20\r\n" +
                "--- End of stack trace from previous location ---\r\n" +
                "   at Foundry.Deploy.Main() in <redacted:path>:line 10",
                [new RemoteDiagnosticException(
                    "System.IO.IOException",
                    "redacted inner",
                    "   at System.Net.Http.HttpClient.Send()\n" +
                    "   at Foundry.Deploy.Download() in <redacted:path>:line 40",
                    [])]));

        tracker.Track(record);

        CapturedPostHogEvent captured = Assert.Single(client.Events);
        var exceptions = Assert.IsType<List<Dictionary<string, object>>>(captured.Properties["$exception_list"]);
        Assert.Collection(exceptions,
            outer =>
            {
                Assert.Equal("System.InvalidOperationException", outer["type"]);
                Assert.Collection(GetFrames(outer),
                    frame => AssertFrame(frame, "Foundry.Deploy.Main()", 10),
                    frame => AssertFrame(frame, "Foundry.Deploy.Run()", 20),
                    frame => AssertFrame(frame, "Foundry.Deploy.Validate()", 30));
            },
            inner =>
            {
                Assert.Equal("System.IO.IOException", inner["type"]);
                Assert.Collection(GetFrames(inner),
                    frame => AssertFrame(frame, "Foundry.Deploy.Download()", 40),
                    frame =>
                    {
                        Assert.Equal("System.Net.Http.HttpClient.Send()", frame["function"]);
                        Assert.False(frame.ContainsKey("lineno"));
                    });
            });
        Assert.DoesNotContain("<redacted:path>", captured.SerializedProperties, StringComparison.Ordinal);
    }

    [Fact]
    public void Track_CapturesSanitizedExceptionChainWithoutPersonProfile()
    {
        var client = new RecordingPostHogEventClient();
        var tracker = new PostHogExceptionTracker(client, "install-1");
        var record = new RemoteDiagnosticRecord(
            DateTimeOffset.UtcNow,
            LogEventLevel.Error,
            "Deployment failed",
            new Dictionary<string, object>
            {
                ["service.name"] = "foundry.deploy",
                ["session.id"] = "session-1",
                ["operation.id"] = "operation-1",
                ["failure.operation"] = "windows_optional_features.validate"
            },
            new RemoteDiagnosticException(
                "System.InvalidOperationException",
                "redacted outer",
                "   at Foundry.Deploy.Run() in <redacted:path>:line 10",
                [new RemoteDiagnosticException(
                    "System.IO.IOException",
                    "redacted inner",
                    "   at System.Net.Http.HttpClient.Send()",
                    [])]));

        tracker.Track(record);

        CapturedPostHogEvent captured = Assert.Single(client.Events);
        Assert.Equal("install-1", captured.DistinctId);
        Assert.Equal("$exception", captured.EventName);
        Assert.Equal(false, captured.Properties["$process_person_profile"]);
        Assert.Equal(true, captured.Properties["$geoip_disable"]);
        Assert.Equal("session-1", captured.Properties["$session_id"]);
        Assert.Equal("windows_optional_features.validate", captured.Properties["failure.operation"]);
        Assert.Equal("System.InvalidOperationException", captured.Properties["$exception_type"]);
        Assert.Equal("redacted outer", captured.Properties["$exception_message"]);
        var exceptions = Assert.IsType<List<Dictionary<string, object>>>(captured.Properties["$exception_list"]);
        Assert.Equal(2, exceptions.Count);
        Assert.Equal(true, GetSingleFrame(exceptions[0])["in_app"]);
        Assert.Equal(false, GetSingleFrame(exceptions[1])["in_app"]);
        Assert.DoesNotContain("C:\\", captured.SerializedProperties, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Track_WhenStackIsMissingAndFailureCodeExists_AddsOperationalFingerprint()
    {
        var client = new RecordingPostHogEventClient();
        var tracker = new PostHogExceptionTracker(client, "install-1");
        var record = new RemoteDiagnosticRecord(
            DateTimeOffset.UtcNow,
            LogEventLevel.Error,
            "Deployment failed",
            new Dictionary<string, object>
            {
                ["service.name"] = "foundry.deploy",
                ["failure.code"] = "network_timeout"
            },
            new RemoteDiagnosticException("Foundry.OperationException", "failed", null, []));

        tracker.Track(record);

        CapturedPostHogEvent captured = Assert.Single(client.Events);
        Assert.Contains("network_timeout", Assert.IsType<string>(captured.Properties["$exception_fingerprint"]), StringComparison.Ordinal);
    }

    [Fact]
    public void Track_WhenStackIsMissingWithOnlyFailureReason_UsesOperationalFingerprint()
    {
        var client = new RecordingPostHogEventClient();
        var tracker = new PostHogExceptionTracker(client, "install-1");
        var record = new RemoteDiagnosticRecord(
            DateTimeOffset.UtcNow,
            LogEventLevel.Fatal,
            "Startup failed",
            new Dictionary<string, object>
            {
                ["service.name"] = "foundry.deploy",
                ["failure.reason"] = "configuration",
                ["workflow.step"] = "apply_image"
            },
            new RemoteDiagnosticException("System.IO.InvalidDataException", "failed", null, []));

        tracker.Track(record);

        // A bare failure reason is not a domain failure, so the workflow step is not appended.
        CapturedPostHogEvent captured = Assert.Single(client.Events);
        Assert.Equal(
            "foundry.deploy:System.IO.InvalidDataException::::configuration::",
            captured.Properties["$exception_fingerprint"]);
    }

    [Theory]
    [InlineData("failure.operation")]
    [InlineData("operation.name")]
    [InlineData("process.operation")]
    [InlineData("tool.name")]
    [InlineData("failure.reason")]
    [InlineData("exception.type")]
    public void Track_FallbackFingerprintDistinguishesStableFailureContext(string changedField)
    {
        var client = new RecordingPostHogEventClient();
        var tracker = new PostHogExceptionTracker(client, "install-1");
        for (int index = 0; index < 2; index++)
        {
            var attributes = new Dictionary<string, object> { ["failure.code"] = 21 };
            attributes[changedField] = $"value-{index}";
            tracker.Track(new RemoteDiagnosticRecord(
                DateTimeOffset.UtcNow, LogEventLevel.Error, "failed", attributes,
                new RemoteDiagnosticException(changedField == "exception.type" ? $"Exception{index}" : "Exception", "failed", null, [])));
        }

        Assert.NotEqual(client.Events[0].Properties["$exception_fingerprint"], client.Events[1].Properties["$exception_fingerprint"]);
    }

    [Fact]
    public void Track_FallbackFingerprintIsStableAcrossSessionsWithoutFailureCode()
    {
        var client = new RecordingPostHogEventClient();
        var tracker = new PostHogExceptionTracker(client, "install-1");
        for (int index = 0; index < 2; index++)
        {
            tracker.Track(new RemoteDiagnosticRecord(
                DateTimeOffset.UtcNow, LogEventLevel.Error, "failed",
                new Dictionary<string, object> { ["session.id"] = $"session-{index}", ["operation.id"] = $"operation-{index}" },
                new RemoteDiagnosticException("Exception", $"message-{index}", null, [])));
        }

        Assert.Equal(client.Events[0].Properties["$exception_fingerprint"], client.Events[1].Properties["$exception_fingerprint"]);
    }

    [Fact]
    public void Track_WhenDomainFailureHasStackTrace_FingerprintsFailureClassificationAndStep()
    {
        var client = new RecordingPostHogEventClient();
        var tracker = new PostHogExceptionTracker(client, "install-1");

        tracker.Track(CreateDomainFailureRecord("WINPE_BUILD_FAILED", "build_winpe"));

        CapturedPostHogEvent captured = Assert.Single(client.Events);
        Assert.Equal(
            "foundry.osd:Foundry.ViewModels.StartMediaViewModel+WinPeOperationException:::copype:tool_failed:WINPE_BUILD_FAILED:1:build_winpe",
            captured.Properties["$exception_fingerprint"]);
    }

    [Theory]
    [InlineData("WINPE_BUILD_FAILED", "build_winpe", "USB_IDENTITY_MISMATCH", "build_winpe")]
    [InlineData("WINPE_BUILD_FAILED", "build_winpe", "WINPE_BUILD_FAILED", "write_usb")]
    public void Track_WhenDomainFailuresShareThrowSite_SeparatesFailureCodesAndSteps(
        string firstCode,
        string firstStep,
        string secondCode,
        string secondStep)
    {
        var client = new RecordingPostHogEventClient();
        var tracker = new PostHogExceptionTracker(client, "install-1");

        tracker.Track(CreateDomainFailureRecord(firstCode, firstStep));
        tracker.Track(CreateDomainFailureRecord(secondCode, secondStep));

        Assert.NotEqual(client.Events[0].Properties["$exception_fingerprint"], client.Events[1].Properties["$exception_fingerprint"]);
    }

    [Fact]
    public void Track_WhenStackExistsWithOnlyFailureReason_KeepsStackBasedGrouping()
    {
        var client = new RecordingPostHogEventClient();
        var tracker = new PostHogExceptionTracker(client, "install-1");
        var record = new RemoteDiagnosticRecord(
            DateTimeOffset.UtcNow,
            LogEventLevel.Fatal,
            "Startup failed",
            new Dictionary<string, object>
            {
                ["service.name"] = "foundry.deploy",
                ["failure.reason"] = "dispatcher"
            },
            new RemoteDiagnosticException("System.NullReferenceException", "failed", "   at Foundry.Deploy.Run() in <redacted:path>:line 10", []));

        tracker.Track(record);

        CapturedPostHogEvent captured = Assert.Single(client.Events);
        Assert.False(captured.Properties.ContainsKey("$exception_fingerprint"));
    }

    [Fact]
    public void Track_WhenStackExistsWithoutFailureAttributes_KeepsStackBasedGrouping()
    {
        var client = new RecordingPostHogEventClient();
        var tracker = new PostHogExceptionTracker(client, "install-1");
        var record = new RemoteDiagnosticRecord(
            DateTimeOffset.UtcNow,
            LogEventLevel.Error,
            "Unexpected failure",
            new Dictionary<string, object>
            {
                ["service.name"] = "foundry.deploy",
                ["workflow.step"] = "apply_image",
                ["tool.name"] = "dism"
            },
            new RemoteDiagnosticException("System.NullReferenceException", "failed", "   at Foundry.Deploy.Run() in <redacted:path>:line 10", []));

        tracker.Track(record);

        CapturedPostHogEvent captured = Assert.Single(client.Events);
        Assert.False(captured.Properties.ContainsKey("$exception_fingerprint"));
    }

    [Fact]
    public void Track_DomainFailureFingerprintExcludesHighCardinalityValues()
    {
        var client = new RecordingPostHogEventClient();
        var tracker = new PostHogExceptionTracker(client, "install-1");
        for (int index = 0; index < 2; index++)
        {
            LogEvent source = RemoteDiagnosticsTestData.LogEvent(LogEventLevel.Error,
                "Deployment operation finished. OperationId={OperationId}, DurationMs={DurationMs}, FailureSummary={FailureSummary}",
                CreateThrownException($"message-{index}"),
                ("OperationId", $"operation-{index}"), ("DurationMs", 1000 + index),
                ("FailureSummary", $"summary-{index}"), ("CompletedStepCount", 3 + index),
                ("FailedStepName", "postinstall_preflight"), ("FailedOperationName", "postinstall.preflight"),
                ("FailureKind", "validation"), ("FailureReason", "postinstall_preflight_failed"),
                ("FailureCode", "POSTINSTALL_PREFLIGHT_FAILED"));
            tracker.Track(RemoteDiagnosticPropertyPolicy.CreateSanitizedRecord(source, RemoteDiagnosticsTestData.Context()));
        }

        string fingerprint = Assert.IsType<string>(client.Events[0].Properties["$exception_fingerprint"]);
        Assert.Equal(fingerprint, client.Events[1].Properties["$exception_fingerprint"]);
        Assert.Contains("POSTINSTALL_PREFLIGHT_FAILED", fingerprint, StringComparison.Ordinal);
        Assert.EndsWith(":postinstall_preflight", fingerprint, StringComparison.Ordinal);
        foreach (string value in new[] { "operation-", "session-1", "message-", "summary-", "1000", "install-1" })
        {
            Assert.DoesNotContain(value, fingerprint, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Track_WhenRecordHasNoException_DoesNotCaptureEvent()
    {
        var client = new RecordingPostHogEventClient();
        var tracker = new PostHogExceptionTracker(client, "install-1");
        var record = new RemoteDiagnosticRecord(
            DateTimeOffset.UtcNow,
            LogEventLevel.Error,
            "Handled operation failure",
            new Dictionary<string, object>(),
            Exception: null);

        tracker.Track(record);

        Assert.Empty(client.Events);
    }

    [Fact]
    public void Track_WhenWarningHasException_DoesNotCreateErrorTrackingIssue()
    {
        var client = new RecordingPostHogEventClient();
        var tracker = new PostHogExceptionTracker(client, "install-1");
        var record = new RemoteDiagnosticRecord(
            DateTimeOffset.UtcNow,
            LogEventLevel.Warning,
            "Handled fallback",
            new Dictionary<string, object>(),
            new RemoteDiagnosticException("System.IOException", "Handled fallback", null, []));

        tracker.Track(record);

        Assert.Empty(client.Events);
    }

    private sealed class RecordingPostHogEventClient : IPostHogEventClient
    {
        public List<CapturedPostHogEvent> Events { get; } = [];

        public bool Capture(string distinctId, string eventName, Dictionary<string, object> properties, DateTimeOffset timestamp)
        {
            Events.Add(new CapturedPostHogEvent(distinctId, eventName, properties));
            return true;
        }

        public Task FlushAsync() => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static RemoteDiagnosticRecord CreateDomainFailureRecord(string failureCode, string step) => new(
        DateTimeOffset.UtcNow,
        LogEventLevel.Error,
        "Final boot media operation failed",
        new Dictionary<string, object>
        {
            ["service.name"] = "foundry.osd",
            ["session.id"] = Guid.NewGuid().ToString("N"),
            ["operation.id"] = Guid.NewGuid().ToString("N"),
            ["duration.ms"] = 4200,
            ["failure.kind"] = "tool",
            ["failure.reason"] = "tool_failed",
            ["failure.code"] = failureCode,
            ["failure.summary"] = "copype failed for C:\\Users\\alice",
            ["workflow.step"] = step,
            ["tool.name"] = "copype",
            ["process.exit_code"] = 1
        },
        new RemoteDiagnosticException(
            "Foundry.ViewModels.StartMediaViewModel+WinPeOperationException",
            "failed",
            "   at Foundry.ViewModels.StartMediaViewModel.EnsureSuccess() in <redacted:path>:line 10",
            []));

    private static InvalidOperationException CreateThrownException(string message)
    {
        try
        {
            throw new InvalidOperationException(message);
        }
        catch (InvalidOperationException exception)
        {
            return exception;
        }
    }

    private static Dictionary<string, object> GetSingleFrame(Dictionary<string, object> exception)
        => Assert.Single(GetFrames(exception));

    private static List<Dictionary<string, object>> GetFrames(Dictionary<string, object> exception)
    {
        var stackTrace = Assert.IsType<Dictionary<string, object>>(exception["stacktrace"]);
        return Assert.IsType<List<Dictionary<string, object>>>(stackTrace["frames"]);
    }

    private static void AssertFrame(Dictionary<string, object> frame, string function, int lineNumber)
    {
        Assert.Equal(function, frame["function"]);
        Assert.Equal(lineNumber, frame["lineno"]);
        Assert.False(frame.ContainsKey("filename"));
        Assert.False(frame.ContainsKey("abs_path"));
    }

    private sealed record CapturedPostHogEvent(
        string DistinctId,
        string EventName,
        Dictionary<string, object> Properties)
    {
        public string SerializedProperties => System.Text.Json.JsonSerializer.Serialize(Properties);
    }
}
