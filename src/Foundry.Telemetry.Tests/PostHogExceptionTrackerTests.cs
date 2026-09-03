// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Serilog.Events;
using Foundry.Telemetry;

namespace Foundry.Telemetry.Tests;

public sealed class PostHogExceptionTrackerTests
{
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
                ["operation.id"] = "operation-1"
            },
            new RemoteDiagnosticException(
                "System.InvalidOperationException",
                "redacted outer",
                "   at Foundry.Deploy.Run() in <redacted:path>:line 10",
                [new RemoteDiagnosticException("System.IO.IOException", "redacted inner", null, [])]));

        tracker.Track(record);

        CapturedPostHogEvent captured = Assert.Single(client.Events);
        Assert.Equal("install-1", captured.DistinctId);
        Assert.Equal("$exception", captured.EventName);
        Assert.Equal(false, captured.Properties["$process_person_profile"]);
        Assert.Equal(true, captured.Properties["$geoip_disable"]);
        Assert.Equal("session-1", captured.Properties["$session_id"]);
        Assert.Equal("System.InvalidOperationException", captured.Properties["$exception_type"]);
        Assert.Equal("redacted outer", captured.Properties["$exception_message"]);
        var exceptions = Assert.IsType<List<Dictionary<string, object>>>(captured.Properties["$exception_list"]);
        Assert.Equal(2, exceptions.Count);
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

    private sealed record CapturedPostHogEvent(
        string DistinctId,
        string EventName,
        Dictionary<string, object> Properties)
    {
        public string SerializedProperties => System.Text.Json.JsonSerializer.Serialize(Properties);
    }
}
