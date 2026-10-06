// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.RegularExpressions;
using PostHog;
using PostHog.Features;

namespace Foundry.Telemetry;

internal interface IPostHogEventClient : IAsyncDisposable
{
    bool Capture(
        string distinctId,
        string eventName,
        Dictionary<string, object> properties,
        DateTimeOffset timestamp);

    Task FlushAsync();
}

internal sealed class PostHogEventClient(PostHogClient client) : IPostHogEventClient
{
    public bool Capture(
        string distinctId,
        string eventName,
        Dictionary<string, object> properties,
        DateTimeOffset timestamp) =>
        client.Capture(distinctId, eventName, properties, null, (FeatureFlagEvaluations?)null, timestamp);

    public Task FlushAsync() => client.FlushAsync();

    public ValueTask DisposeAsync() => client.DisposeAsync();
}

/// <summary>
/// Builds PostHog Error Tracking events exclusively from sanitized exception records.
/// </summary>
internal sealed partial class PostHogExceptionTracker(
    IPostHogEventClient client,
    string distinctId,
    Action<ExceptionDeliveryFailure>? reportFailure = null)
{
    private static readonly string[] DomainFailureAttributeNames =
        ["failure.code", "failure.reason", "failure.kind", "failure.operation"];

    public void Track(RemoteDiagnosticRecord record)
    {
        if (!record.ShouldTrackException || record.Exception is null || record.Level < Serilog.Events.LogEventLevel.Error)
        {
            return;
        }

        var properties = new Dictionary<string, object>(record.Attributes, StringComparer.Ordinal)
        {
            ["$exception_type"] = record.Exception.Type,
            ["$exception_message"] = record.Exception.Message,
            ["$exception_level"] = record.Level == Serilog.Events.LogEventLevel.Fatal ? "fatal" : "error",
            ["$exception_list"] = CreateExceptionList(record.Exception),
            ["$process_person_profile"] = false,
            ["$geoip_disable"] = true
        };

        if (record.Attributes.TryGetValue("session.id", out object? sessionId))
        {
            properties["$session_id"] = sessionId;
        }

        string? fingerprint = CreateFingerprint(record, record.Exception);
        if (fingerprint is not null)
        {
            properties["$exception_fingerprint"] = fingerprint;
        }

        if (!client.Capture(distinctId, "$exception", properties, record.Timestamp))
        {
            var failure = new ExceptionDeliveryFailure("capture_rejected");
            if (reportFailure is not null) reportFailure(failure);
            else Serilog.Log.Write(failure.CreateLogEvent());
        }
    }

    /// <summary>
    /// Chooses the PostHog issue grouping key. Domain failures are grouped by their stable failure
    /// classification instead of the shared throw site, so distinct failure codes or steps never
    /// collapse into one issue (and are not dropped when an unrelated failure's issue is suppressed).
    /// Exceptions without a stack trace use the operational context; other exceptions return
    /// <see langword="null"/> to keep PostHog's default stack-based grouping.
    /// </summary>
    /// <remarks>
    /// Only sanitized, low-cardinality attributes are used. Messages, summaries, identifiers, paths,
    /// durations, and process output are excluded so the key stays deterministic and bounded.
    /// </remarks>
    private static string? CreateFingerprint(RemoteDiagnosticRecord record, RemoteDiagnosticException exception)
    {
        bool hasDomainFailure = DomainFailureAttributeNames.Any(name => !string.IsNullOrWhiteSpace(GetAttribute(record, name)));
        if (!hasDomainFailure && !string.IsNullOrWhiteSpace(exception.StackTrace))
        {
            return null;
        }

        string operationalFingerprint = string.Join(':',
            GetAttribute(record, "service.name"),
            exception.Type,
            GetLogicalOperation(record),
            GetAttribute(record, "process.operation"),
            GetAttribute(record, "tool.name"),
            GetAttribute(record, "failure.reason"),
            GetAttribute(record, "failure.code"),
            GetAttribute(record, "process.exit_code"));
        return hasDomainFailure
            ? string.Join(':', operationalFingerprint, GetAttribute(record, "workflow.step"))
            : operationalFingerprint;
    }

    private static string GetLogicalOperation(RemoteDiagnosticRecord record)
    {
        foreach (string name in new[] { "failure.operation", "operation.name", "process.operation" })
        {
            string operation = GetAttribute(record, name);
            if (!string.IsNullOrWhiteSpace(operation))
            {
                return operation;
            }
        }

        return string.Empty;
    }

    private static string GetAttribute(RemoteDiagnosticRecord record, string name) =>
        record.Attributes.TryGetValue(name, out object? value)
            ? Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty
            : string.Empty;

    private static List<Dictionary<string, object>> CreateExceptionList(RemoteDiagnosticException exception)
    {
        var exceptions = new List<Dictionary<string, object>>();
        var pending = new Stack<RemoteDiagnosticException>();
        pending.Push(exception);
        while (pending.Count > 0)
        {
            RemoteDiagnosticException current = pending.Pop();
            exceptions.Add(new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["type"] = current.Type,
                ["value"] = current.Message,
                ["mechanism"] = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["type"] = "generic",
                    ["handled"] = true,
                    ["synthetic"] = false
                },
                ["stacktrace"] = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["frames"] = CreateFrames(current.StackTrace),
                    ["type"] = "raw"
                }
            });

            for (int index = current.InnerExceptions.Count - 1; index >= 0; index--)
            {
                pending.Push(current.InnerExceptions[index]);
            }
        }

        return exceptions;
    }

    private static List<Dictionary<string, object>> CreateFrames(string? stackTrace)
    {
        var frames = new List<Dictionary<string, object>>();
        if (string.IsNullOrWhiteSpace(stackTrace))
        {
            return frames;
        }

        foreach (string line in stackTrace.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            Match match = StackFramePattern().Match(line);
            if (!match.Success)
            {
                continue;
            }

            string function = match.Groups["function"].Value.Trim();
            var frame = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["platform"] = "custom",
                ["lang"] = "dotnet",
                ["function"] = function,
                ["module"] = GetModule(function),
                ["in_app"] = IsApplicationFrame(function)
            };
            if (int.TryParse(match.Groups["line"].Value, out int lineNumber))
            {
                frame["lineno"] = lineNumber;
            }

            frames.Add(frame);
        }

        // .NET stacks are crash-first; PostHog requires the entry point first.
        frames.Reverse();
        return frames;
    }

    private static bool IsApplicationFrame(string function) =>
        function.StartsWith("Foundry.", StringComparison.Ordinal) ||
        function.StartsWith("Foundry+", StringComparison.Ordinal);

    private static string GetModule(string function)
    {
        int parameterStart = function.IndexOf('(');
        string method = parameterStart >= 0 ? function[..parameterStart] : function;
        int separator = method.LastIndexOf('.');
        return separator > 0 ? method[..separator] : string.Empty;
    }

    [GeneratedRegex("^\\s*at\\s+(?<function>.+?)(?:\\s+in\\s+<redacted:path>(?::line\\s+(?<line>\\d+))?)?$", RegexOptions.CultureInvariant)]
    private static partial Regex StackFramePattern();
}
