// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.RegularExpressions;
using Foundry.Utilities.Diagnostics;
using Serilog.Events;

namespace Foundry.Telemetry;

/// <summary>
/// Converts Serilog events into a strict, privacy-filtered remote contract.
/// </summary>
public static partial class RemoteDiagnosticPropertyPolicy
{
    private const int MaximumMessageLength = 2048;
    private const int MaximumAttributeLength = 512;
    private const int MaximumStackTraceLength = 16384;
    private const int MaximumExceptionDepth = 4;

    private static readonly IReadOnlyDictionary<string, string> AllowedPropertyNames =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SourceContext"] = "code.namespace",
            ["Component"] = "component",
            ["OperationId"] = "operation.id",
            ["Workflow"] = "workflow.name",
            ["Stage"] = "workflow.stage",
            ["Step"] = "workflow.step",
            ["StepName"] = "workflow.step",
            ["Outcome"] = "operation.outcome",
            ["DurationMs"] = "duration.ms",
            ["DurationMilliseconds"] = "duration.ms",
            ["DurationSeconds"] = "duration.seconds",
            ["RetryCount"] = "retry.count",
            ["Retryable"] = "retryable",
            ["FailureKind"] = "failure.kind",
            ["FailureCode"] = "failure.code",
            ["ErrorCode"] = "failure.code",
            ["FailureReason"] = "failure.reason",
            ["ToolName"] = "tool.name",
            ["ExitCode"] = "process.exit_code",
            ["NetworkOperation"] = "network.operation",
            ["BootMediaTarget"] = "boot_media.target",
            ["UsbOperation"] = "boot_media.usb_operation",
            ["DeploymentMode"] = "deployment.mode",
            ["Mode"] = "deployment.mode",
            ["FailedStepName"] = "workflow.step",
            ["Success"] = "operation.success",
            ["Cancelled"] = "operation.cancelled",
            ["IsDryRun"] = "deployment.dry_run",
            ["CompletedStepCount"] = "workflow.completed_step_count"
        };

    /// <summary>
    /// Creates a record that contains only explicitly approved, sanitized values.
    /// </summary>
    public static RemoteDiagnosticRecord CreateSanitizedRecord(
        LogEvent logEvent,
        RemoteDiagnosticsContext context)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(context);

        var attributes = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["service.name"] = SanitizeAttribute(context.App),
            ["service.version"] = SanitizeAttribute(context.AppVersion),
            ["build.configuration"] = SanitizeAttribute(context.BuildConfiguration),
            ["runtime.name"] = SanitizeAttribute(context.Runtime),
            ["runtime.architecture"] = SanitizeAttribute(context.RuntimeArchitecture),
            ["locale"] = SanitizeAttribute(context.Locale),
            ["session.id"] = SanitizeAttribute(context.SessionId),
            ["service.release"] = SanitizeAttribute(context.Release)
        };

        foreach ((string sourceName, string remoteName) in AllowedPropertyNames)
        {
            if (logEvent.Properties.TryGetValue(sourceName, out LogEventPropertyValue? propertyValue) &&
                TryConvertScalar(propertyValue, out object scalarValue))
            {
                attributes[remoteName] = scalarValue;
            }
        }

        string body = SanitizeRemoteText(logEvent.MessageTemplate.Text, MaximumMessageLength);
        RemoteDiagnosticException? exception = CreateException(logEvent.Exception, depth: 0);
        return new RemoteDiagnosticRecord(logEvent.Timestamp, logEvent.Level, body, attributes, exception);
    }

    private static bool TryConvertScalar(LogEventPropertyValue propertyValue, out object value)
    {
        value = null!;
        if (propertyValue is not ScalarValue { Value: { } scalar })
        {
            return false;
        }

        switch (scalar)
        {
            case string text:
                value = SanitizeAttribute(text);
                return true;
            case char character:
                value = character.ToString();
                return true;
            case bool:
            case byte:
            case sbyte:
            case short:
            case ushort:
            case int:
            case uint:
            case long:
            case ulong:
            case float:
            case double:
            case decimal:
                value = scalar;
                return true;
            case Enum enumValue:
                value = enumValue.ToString();
                return true;
            default:
                return false;
        }
    }

    private static RemoteDiagnosticException? CreateException(Exception? exception, int depth)
    {
        if (exception is null || depth >= MaximumExceptionDepth)
        {
            return null;
        }

        string? stackTrace = exception.StackTrace is null
            ? null
            : SanitizeStackTrace(exception.StackTrace);
        return new RemoteDiagnosticException(
            exception.GetType().FullName ?? exception.GetType().Name,
            SanitizeRemoteText(exception.Message, MaximumMessageLength),
            stackTrace,
            GetInnerExceptions(exception)
                .Select(innerException => CreateException(innerException, depth + 1))
                .OfType<RemoteDiagnosticException>()
                .ToArray());
    }

    private static IEnumerable<Exception> GetInnerExceptions(Exception exception) =>
        exception is AggregateException aggregateException
            ? aggregateException.InnerExceptions
            : exception.InnerException is { } innerException
                ? [innerException]
                : [];

    private static string SanitizeAttribute(string? value) =>
        SanitizeRemoteText(value, MaximumAttributeLength);

    internal static string SanitizeResourceValue(string? value) => SanitizeAttribute(value);

    private static string SanitizeStackTrace(string stackTrace)
    {
        string withoutSourcePaths = StackSourcePathPattern().Replace(stackTrace, " in <redacted:path>");
        return DiagnosticContentSanitizer.SanitizeMultiline(withoutSourcePaths, MaximumStackTraceLength);
    }

    private static string SanitizeRemoteText(string? value, int maximumLength)
    {
        string withoutUris = UriPattern().Replace(value ?? string.Empty, "<redacted:uri>");
        string withoutPaths = WindowsPathPattern().Replace(withoutUris, "<redacted:path>");
        return DiagnosticContentSanitizer.Sanitize(withoutPaths, maximumLength);
    }

    [GeneratedRegex("https?://[^\\s\\\"'<>]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UriPattern();

    [GeneratedRegex("(?<![A-Za-z0-9])(?:[A-Za-z]:\\\\|\\\\\\\\)[^\\r\\n\\\"<>|]+", RegexOptions.CultureInvariant)]
    private static partial Regex WindowsPathPattern();

    [GeneratedRegex("\\s+in\\s+(?:[A-Za-z]:\\\\|\\\\\\\\)[^\\r\\n]+?(?=:line\\s+\\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StackSourcePathPattern();
}
