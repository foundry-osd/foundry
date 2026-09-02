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
    string distinctId)
{
    public void Track(RemoteDiagnosticRecord record)
    {
        if (record.Exception is null)
        {
            return;
        }

        var properties = new Dictionary<string, object>(record.Attributes, StringComparer.Ordinal)
        {
            ["$exception_type"] = record.Exception.Type,
            ["$exception_message"] = record.Exception.Message,
            ["$exception_level"] = record.Level == Serilog.Events.LogEventLevel.Fatal ? "fatal" : "error",
            ["$exception_list"] = CreateExceptionList(record.Exception),
            ["$process_person_profile"] = false
        };

        if (record.Attributes.TryGetValue("session.id", out object? sessionId))
        {
            properties["$session_id"] = sessionId;
        }

        if (string.IsNullOrWhiteSpace(record.Exception.StackTrace) &&
            record.Attributes.TryGetValue("failure.code", out object? failureCode))
        {
            string serviceName = record.Attributes.TryGetValue("service.name", out object? service)
                ? service.ToString() ?? "foundry"
                : "foundry";
            properties["$exception_fingerprint"] = $"{serviceName}:{failureCode}";
        }

        client.Capture(distinctId, "$exception", properties, record.Timestamp);
    }

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
                ["module"] = GetModule(function)
            };
            if (int.TryParse(match.Groups["line"].Value, out int lineNumber))
            {
                frame["lineno"] = lineNumber;
            }

            frames.Add(frame);
        }

        return frames;
    }

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
