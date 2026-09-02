// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Serilog.Events;
using Serilog.Parsing;
using Foundry.Telemetry;

namespace Foundry.Telemetry.Tests;

public sealed class RemoteDiagnosticPropertyPolicyTests
{
    [Fact]
    public void CreateSanitizedRecord_RemovesUnknownAndSensitiveProperties()
    {
        LogEvent source = CreateLogEvent(
            LogEventLevel.Error,
            "Deployment failed for {Path}",
            new InvalidOperationException("token=secret"),
            ("Path", "C:\\Users\\alice\\secret.wim"),
            ("OperationId", "operation-1"),
            ("RetryCount", 2),
            ("UnknownProperty", "private-value"));

        RemoteDiagnosticRecord result = RemoteDiagnosticPropertyPolicy.CreateSanitizedRecord(source, CreateContext());

        Assert.Equal("operation-1", result.Attributes["operation.id"]);
        Assert.Equal(2, result.Attributes["retry.count"]);
        Assert.DoesNotContain("Path", result.Attributes.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("UnknownProperty", result.Attributes.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("alice", result.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", result.Body, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(result.Exception);
        Assert.DoesNotContain("secret", result.Exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateSanitizedRecord_IncludesStableContextAndOnlyScalarAllowedValues()
    {
        LogEvent source = CreateLogEvent(
            LogEventLevel.Warning,
            "Network operation failed",
            exception: null,
            ("Workflow", "deploy"),
            ("Stage", "download"),
            ("Outcome", "failed"),
            ("FailureKind", "network"),
            ("FailureReason", "timeout"),
            ("ErrorSummary", @"token=plain-text C:\Users\operator\file.txt"),
            ("Retryable", true),
            ("Unsupported", new[] { "one", "two" }));

        RemoteDiagnosticRecord result = RemoteDiagnosticPropertyPolicy.CreateSanitizedRecord(source, CreateContext());

        Assert.Equal("foundry.deploy", result.Attributes["service.name"]);
        Assert.Equal("1.2.3", result.Attributes["service.version"]);
        Assert.Equal("release", result.Attributes["build.configuration"]);
        Assert.Equal("winpe", result.Attributes["runtime.name"]);
        Assert.Equal("x64", result.Attributes["runtime.architecture"]);
        Assert.Equal("fr-FR", result.Attributes["locale"]);
        Assert.Equal("session-1", result.Attributes["session.id"]);
        Assert.Equal("foundry.deploy@1.2.3", result.Attributes["service.release"]);
        Assert.Equal("deploy", result.Attributes["workflow.name"]);
        Assert.Equal("download", result.Attributes["workflow.stage"]);
        Assert.Equal("failed", result.Attributes["operation.outcome"]);
        Assert.Equal("network", result.Attributes["failure.kind"]);
        Assert.Equal("timeout", result.Attributes["failure.reason"]);
        Assert.DoesNotContain("plain-text", result.Attributes["failure.summary"].ToString());
        Assert.DoesNotContain(@"C:\Users\operator", result.Attributes["failure.summary"].ToString());
        Assert.Equal(true, result.Attributes["retryable"]);
        Assert.DoesNotContain("Unsupported", result.Attributes.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateSanitizedRecord_BoundsMessageAndExceptionFields()
    {
        string longValue = new('x', 5000);
        LogEvent source = CreateLogEvent(
            LogEventLevel.Error,
            longValue,
            new InvalidOperationException(longValue));

        RemoteDiagnosticRecord result = RemoteDiagnosticPropertyPolicy.CreateSanitizedRecord(source, CreateContext());

        Assert.EndsWith("<truncated>", result.Body, StringComparison.Ordinal);
        Assert.True(result.Body.Length <= 2048);
        Assert.NotNull(result.Exception);
        Assert.EndsWith("<truncated>", result.Exception.Message, StringComparison.Ordinal);
        Assert.True(result.Exception.Message.Length <= 2048);
    }

    [Fact]
    public void CreateSanitizedRecord_RedactsLiteralPathsAndUris()
    {
        LogEvent source = CreateLogEvent(
            LogEventLevel.Error,
            "Request to https://example.test/api?token=secret failed for C:\\Users\\alice\\secret.wim",
            new InvalidOperationException("Failed at C:\\Users\\alice\\secret.wim"));

        RemoteDiagnosticRecord result = RemoteDiagnosticPropertyPolicy.CreateSanitizedRecord(source, CreateContext());

        Assert.Contains("<redacted:uri>", result.Body, StringComparison.Ordinal);
        Assert.Contains("<redacted:path>", result.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("example.test", result.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alice", result.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret.wim", result.Exception!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateSanitizedRecord_MapsExistingOperationPropertyNames()
    {
        LogEvent source = CreateLogEvent(
            LogEventLevel.Error,
            "Operation failed",
            exception: null,
            ("Mode", "interactive"),
            ("ErrorCode", "network_timeout"),
            ("FailedStepName", "download_image"),
            ("UsbOperation", "create"),
            ("Success", false),
            ("Cancelled", false),
            ("IsDryRun", true),
            ("CompletedStepCount", 3),
            ("Target", "C:\\Users\\alice\\secret.iso"));

        RemoteDiagnosticRecord result = RemoteDiagnosticPropertyPolicy.CreateSanitizedRecord(source, CreateContext());

        Assert.Equal("interactive", result.Attributes["deployment.mode"]);
        Assert.Equal("network_timeout", result.Attributes["failure.code"]);
        Assert.Equal("download_image", result.Attributes["workflow.step"]);
        Assert.Equal("create", result.Attributes["boot_media.usb_operation"]);
        Assert.Equal(false, result.Attributes["operation.success"]);
        Assert.Equal(false, result.Attributes["operation.cancelled"]);
        Assert.Equal(true, result.Attributes["deployment.dry_run"]);
        Assert.Equal(3, result.Attributes["workflow.completed_step_count"]);
        Assert.DoesNotContain("Target", result.Attributes.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateSanitizedRecord_PreservesAggregateExceptionBranches()
    {
        var exception = new AggregateException(
            "multiple failures",
            new IOException("first"),
            new InvalidOperationException("second"));
        LogEvent source = CreateLogEvent(LogEventLevel.Error, "Operation failed", exception);

        RemoteDiagnosticRecord result = RemoteDiagnosticPropertyPolicy.CreateSanitizedRecord(source, CreateContext());

        Assert.NotNull(result.Exception);
        Assert.Equal(2, result.Exception.InnerExceptions.Count);
        Assert.Equal("System.IO.IOException", result.Exception.InnerExceptions[0].Type);
        Assert.Equal("System.InvalidOperationException", result.Exception.InnerExceptions[1].Type);
    }

    private static RemoteDiagnosticsContext CreateContext() => new(
        App: "foundry.deploy",
        AppVersion: "1.2.3",
        BuildConfiguration: "release",
        Runtime: "winpe",
        RuntimeArchitecture: "x64",
        Locale: "fr-FR",
        SessionId: "session-1",
        Release: "foundry.deploy@1.2.3");

    private static LogEvent CreateLogEvent(
        LogEventLevel level,
        string messageTemplate,
        Exception? exception,
        params (string Name, object Value)[] properties)
    {
        return new LogEvent(
            DateTimeOffset.UtcNow,
            level,
            exception,
            new MessageTemplateParser().Parse(messageTemplate),
            properties.Select(static property => new LogEventProperty(property.Name, new ScalarValue(property.Value))));
    }
}
