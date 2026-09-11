// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Serilog.Events;
using Serilog.Parsing;

namespace Foundry.Utilities.Diagnostics;

/// <summary>Produces the common secret-masked event before local and remote sinks receive it.</summary>
public static class LogEventNormalizer
{
    private static readonly string ProcessId = Guid.NewGuid().ToString("N");
    private static long _sequence;

    /// <summary>Preserves event time, trace context, structure and identity across repeated normalization.</summary>
    public static LogEvent Normalize(LogEvent source)
    {
        ArgumentNullException.ThrowIfNull(source);
        HashSet<string> credentialProperties = LogSecretMasker.GetSecretTemplateProperties(source.MessageTemplate.Text);
        var properties = source.Properties.Select(property => new LogEventProperty(property.Key,
            credentialProperties.Contains(property.Key) ? new ScalarValue(LogSecretMasker.Redacted)
                : MaskProperty(property.Key, property.Value))).ToList();
        string template = LogSecretMasker.Mask(source.MessageTemplate.Text);
        var result = new LogEvent(source.Timestamp, source.Level, LogExceptionSnapshot.Create(source.Exception),
            template == source.MessageTemplate.Text ? source.MessageTemplate : new MessageTemplateParser().Parse(template),
            properties, source.TraceId ?? default, source.SpanId ?? default);
        result.AddPropertyIfAbsent(new LogEventProperty("diagnostics.record_id", new ScalarValue(Guid.NewGuid().ToString("N"))));
        result.AddPropertyIfAbsent(new LogEventProperty("diagnostics.process_id", new ScalarValue(ProcessId)));
        result.AddPropertyIfAbsent(new LogEventProperty("diagnostics.sequence", new ScalarValue(Interlocked.Increment(ref _sequence))));
        DiagnosticClock.Current.Enrich(result);
        return result;
    }

    private static LogEventPropertyValue MaskProperty(string name, LogEventPropertyValue value)
    {
        if (LogSecretMasker.IsSecretName(name))
        {
            return new ScalarValue(LogSecretMasker.Redacted);
        }

        return value switch
        {
            ScalarValue { Value: string text } => new ScalarValue(LogSecretMasker.Mask(text)),
            ScalarValue { Value: Uri uri } => new ScalarValue(LogSecretMasker.Mask(uri.ToString())),
            SequenceValue sequence => new SequenceValue(sequence.Elements.Select(element => MaskProperty(string.Empty, element))),
            StructureValue structure => new StructureValue(structure.Properties.Select(property =>
                new LogEventProperty(property.Name, MaskProperty(property.Name, property.Value))), structure.TypeTag),
            DictionaryValue dictionary => MaskDictionary(dictionary),
            _ => value
        };
    }

    private static DictionaryValue MaskDictionary(DictionaryValue dictionary)
    {
        var entries = new Dictionary<ScalarValue, LogEventPropertyValue>();
        foreach ((ScalarValue key, LogEventPropertyValue value) in dictionary.Elements)
        {
            string original = key.Value?.ToString() ?? string.Empty;
            string masked = LogSecretMasker.Mask(original);
            ScalarValue safeKey = masked == original ? key : new ScalarValue(masked);
            int occurrence = 1;
            while (entries.ContainsKey(safeKey))
            {
                safeKey = new ScalarValue(masked + " [" + (++occurrence).ToString(System.Globalization.CultureInfo.InvariantCulture) + "]");
            }
            entries.Add(safeKey, MaskProperty(original, value));
        }
        return new DictionaryValue(entries);
    }
}
