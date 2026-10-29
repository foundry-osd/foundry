// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Logging;

namespace Foundry.Connect.Tests.Support;

/// <summary>
/// One captured log call: rendered message, state properties merged with the active scope properties, and the exception.
/// </summary>
internal sealed record RecordedLogEntry(
    LogLevel Level,
    string Message,
    IReadOnlyDictionary<string, object?> Properties,
    Exception? Exception);

/// <summary>
/// Captures log calls. State properties override scope properties of the same name.
/// </summary>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly List<IReadOnlyList<KeyValuePair<string, object?>>> _scopes = [];

    // Guarded by lock (Entries).
    public List<RecordedLogEntry> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        // KeyValuePair<string, object> and KeyValuePair<string, object?> are the same runtime type.
        KeyValuePair<string, object?>[] properties = state is IEnumerable<KeyValuePair<string, object?>> pairs ? [.. pairs] : [];

        lock (Entries)
        {
            _scopes.Add(properties);
        }

        return new ScopeToken(this, properties);
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var properties = new Dictionary<string, object?>(StringComparer.Ordinal);
        lock (Entries)
        {
            foreach (IReadOnlyList<KeyValuePair<string, object?>> scope in _scopes)
            {
                foreach ((string name, object? value) in scope)
                {
                    properties[name] = value;
                }
            }
        }

        if (state is IEnumerable<KeyValuePair<string, object?>> stateProperties)
        {
            foreach ((string name, object? value) in stateProperties)
            {
                if (name != "{OriginalFormat}")
                {
                    properties[name] = value;
                }
            }
        }

        lock (Entries)
        {
            Entries.Add(new RecordedLogEntry(logLevel, formatter(state, exception), properties, exception));
        }
    }

    private sealed class ScopeToken(RecordingLogger<T> owner, IReadOnlyList<KeyValuePair<string, object?>> scope) : IDisposable
    {
        public void Dispose()
        {
            lock (owner.Entries)
            {
                owner._scopes.Remove(scope);
            }
        }
    }
}
