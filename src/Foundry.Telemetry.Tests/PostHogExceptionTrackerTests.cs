// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Serilog.Events;
using Foundry.Telemetry;

namespace Foundry.Telemetry.Tests;

public sealed class PostHogExceptionTrackerTests
{
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
        Assert.Equal("foundry.deploy:network_timeout", captured.Properties["$exception_fingerprint"]);
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
