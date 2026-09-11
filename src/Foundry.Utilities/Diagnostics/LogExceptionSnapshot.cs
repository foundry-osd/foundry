// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.CompilerServices;

namespace Foundry.Utilities.Diagnostics;

/// <summary>Retains an exception's original diagnostic identity and stack after secret removal.</summary>
public sealed class LogExceptionSnapshot : Exception
{
    private static readonly ConditionalWeakTable<Exception, LogExceptionSnapshot> Snapshots = new();
    private readonly string _text;

    private LogExceptionSnapshot(Exception source, IReadOnlyList<Exception> innerExceptions)
        : base(LogSecretMasker.Mask(source.Message), innerExceptions.FirstOrDefault())
    {
        OriginalType = source is LogExceptionSnapshot snapshot ? snapshot.OriginalType : source.GetType().FullName ?? source.GetType().Name;
        StackTrace = source.StackTrace is { } stack ? LogSecretMasker.Mask(stack) : null;
        InnerExceptions = innerExceptions;
        HResult = source.HResult;
        _text = LogSecretMasker.Mask(source.ToString());
    }

    public string OriginalType { get; }
    public IReadOnlyList<Exception> InnerExceptions { get; }
    public override string? StackTrace { get; }

    public override string ToString()
    {
        return _text;
    }

    /// <summary>Leaves unaffected exceptions intact; snapshots only chains containing secrets.</summary>
    internal static Exception? Create(Exception? source)
    {
        if (source is null || source is LogExceptionSnapshot)
        {
            return source;
        }

        string original = source.ToString();
        if (string.Equals(original, LogSecretMasker.Mask(original), StringComparison.Ordinal))
        {
            return source;
        }

        return Snapshots.GetValue(source, static exception =>
        {
            IEnumerable<Exception> children = exception is AggregateException aggregate ? aggregate.InnerExceptions
                : exception.InnerException is { } inner ? [inner] : [];
            return new LogExceptionSnapshot(exception, children.Select(child => Create(child)!).ToArray());
        });
    }
}
