// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.IO;
using System.Text.Json;
using Foundry.Utilities.Diagnostics;
using Serilog.Events;
using Serilog.Formatting.Display;

namespace Foundry.Telemetry;

/// <summary>Converts common log events into the full-fidelity Logs contract.</summary>
public static class LogRecordFactory
{
    private static readonly MessageTemplateTextFormatter MessageFormatter = new("{Message:lj}", CultureInfo.InvariantCulture);

    /// <summary>Revalidates persisted Logs without applying the separate Error Tracking whitelist.</summary>
    public static RemoteDiagnosticRecord SanitizePersistedRecord(RemoteDiagnosticRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return record with
        {
            Body = LogSecretMasker.Mask(record.Body),
            Attributes = MaskPersistedObject(record.Attributes.Select(entry =>
                new KeyValuePair<string, JsonElement>(entry.Key, JsonSerializer.SerializeToElement(entry.Value)))),
            Exception = MaskPersistedException(record.Exception),
            ShouldTrackException = false
        };
    }

    private static object? MaskPersistedValue(string name, JsonElement value)
    {
        if (LogSecretMasker.IsSecretName(name))
        {
            return LogSecretMasker.Redacted;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Object => MaskPersistedObject(value.EnumerateObject().Select(property =>
                new KeyValuePair<string, JsonElement>(property.Name, property.Value))),
            JsonValueKind.Array => value.EnumerateArray().Select(item => MaskPersistedValue(string.Empty, item)).ToArray(),
            JsonValueKind.String => LogSecretMasker.Mask(value.GetString()!),
            JsonValueKind.Number when value.TryGetInt64(out long number) => number,
            JsonValueKind.Number when value.TryGetDecimal(out decimal number) => number,
            JsonValueKind.Number => ConvertScalar(value.GetDouble()),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static RemoteDiagnosticException? MaskPersistedException(RemoteDiagnosticException? exception)
    {
        if (exception is null)
        {
            return null;
        }
        if (exception.Type is null || exception.Message is null || exception.InnerExceptions is null ||
            exception.InnerExceptions.Any(child => child is null))
        {
            throw new JsonException("A persisted exception requires a type, message and valid child collection.");
        }
        return exception with
        {
            Type = LogSecretMasker.Mask(exception.Type),
            Message = LogSecretMasker.Mask(exception.Message),
            StackTrace = exception.StackTrace is { } stack ? LogSecretMasker.Mask(stack) : null,
            InnerExceptions = exception.InnerExceptions.Select(child => MaskPersistedException(child)!).ToArray()
        };
    }

    private static Dictionary<string, object> MaskPersistedObject(IEnumerable<KeyValuePair<string, JsonElement>> source)
    {
        var result = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach ((string name, JsonElement value) in source)
        {
            string masked = LogSecretMasker.Mask(name);
            string key = masked;
            int occurrence = 1;
            while (result.ContainsKey(key))
            {
                key = masked + " [" + (++occurrence).ToString(CultureInfo.InvariantCulture) + "]";
            }
            result.Add(key, MaskPersistedValue(name, value)!);
        }
        return result;
    }

    /// <summary>Preserves original time, severity and structured properties for Logs export.</summary>
    public static RemoteDiagnosticRecord Create(LogEvent logEvent, RemoteDiagnosticsContext context)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(context);
        LogEvent source = LogEventNormalizer.Normalize(logEvent);
        var attributes = source.Properties.ToDictionary(property => property.Key,
            property => ConvertValue(property.Value)!, StringComparer.Ordinal);
        attributes["service.name"] = LogSecretMasker.Mask(context.App);
        attributes["service.version"] = LogSecretMasker.Mask(context.AppVersion);
        attributes["build.configuration"] = LogSecretMasker.Mask(context.BuildConfiguration);
        attributes["runtime.name"] = LogSecretMasker.Mask(context.Runtime);
        attributes["runtime.architecture"] = LogSecretMasker.Mask(context.RuntimeArchitecture);
        attributes["locale"] = LogSecretMasker.Mask(context.Locale);
        attributes["session.id"] = LogSecretMasker.Mask(context.SessionId);
        attributes["service.release"] = LogSecretMasker.Mask(context.Release);
        attributes["message_template.text"] = source.MessageTemplate.Text;
        AddAlias(attributes, "SourceContext", "code.namespace");
        AddAlias(attributes, "Component", "component");
        AddAlias(attributes, "OperationId", "operation.id");
        AddAlias(attributes, "ProcessStdout", "process.stdout");
        AddAlias(attributes, "ProcessStderr", "process.stderr");
        if (source.TraceId is { } traceId)
        {
            attributes["trace.id"] = traceId.ToString();
        }
        if (source.SpanId is { } spanId)
        {
            attributes["span.id"] = spanId.ToString();
        }

        using var message = new StringWriter(CultureInfo.InvariantCulture);
        MessageFormatter.Format(source, message);
        return new RemoteDiagnosticRecord(source.Timestamp, source.Level,
            message.ToString(), attributes, ConvertException(source.Exception))
        {
            // Logs and Error Tracking have separate privacy and duplicate-suppression contracts.
            ShouldTrackException = false
        };
    }

    private static void AddAlias(Dictionary<string, object> attributes, string source, string destination)
    {
        if (attributes.TryGetValue(source, out object? value))
        {
            attributes[destination] = value;
        }
    }

    private static object? ConvertValue(LogEventPropertyValue value)
    {
        return value switch
        {
            ScalarValue scalar => ConvertScalar(scalar.Value),
            SequenceValue sequence => sequence.Elements.Select(ConvertValue).ToArray(),
            StructureValue structure => structure.Properties.ToDictionary(property => property.Name,
                property => ConvertValue(property.Value), StringComparer.Ordinal),
            DictionaryValue dictionary => dictionary.Elements.ToDictionary(entry =>
                Convert.ToString(entry.Key.Value, CultureInfo.InvariantCulture) ?? "null",
                entry => ConvertValue(entry.Value), StringComparer.Ordinal),
            _ => LogSecretMasker.Mask(value.ToString())
        };
    }

    private static object? ConvertScalar(object? value)
    {
        return value switch
        {
            null => null,
            string or bool or byte or sbyte or short or ushort or int or uint or long or ulong or decimal => value,
            float number when float.IsFinite(number) => number,
            double number when double.IsFinite(number) => number,
            DateTime time => time.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset time => time.ToString("O", CultureInfo.InvariantCulture),
            byte[] bytes => Convert.ToBase64String(bytes),
            _ => LogSecretMasker.Mask(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty)
        };
    }

    private static RemoteDiagnosticException? ConvertException(Exception? exception)
    {
        if (exception is null)
        {
            return null;
        }

        IEnumerable<Exception> children = exception switch
        {
            LogExceptionSnapshot snapshot => snapshot.InnerExceptions,
            AggregateException aggregate => aggregate.InnerExceptions,
            { InnerException: { } inner } => [inner],
            _ => []
        };
        return new RemoteDiagnosticException(
            exception is LogExceptionSnapshot safe ? safe.OriginalType : exception.GetType().FullName ?? exception.GetType().Name,
            exception.Message, exception.StackTrace, children.Select(child => ConvertException(child)!).ToArray());
    }
}
