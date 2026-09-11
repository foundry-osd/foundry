// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Serilog.Core;
using Serilog.Events;

namespace Foundry.Utilities.Diagnostics;

/// <summary>Owns the sink logger and applies normalization once before its fan-out.</summary>
internal sealed class NormalizingLogSink(Logger output) : ILogEventSink, IDisposable
{
    public void Emit(LogEvent logEvent)
    {
        output.Write(LogEventNormalizer.Normalize(logEvent));
    }

    public void Dispose()
    {
        output.Dispose();
    }
}
