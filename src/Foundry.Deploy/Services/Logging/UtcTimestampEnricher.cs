// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Serilog.Core;
using Serilog.Events;

namespace Foundry.Deploy.Services.Logging;

internal sealed class UtcTimestampEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        LogEventProperty property = propertyFactory.CreateProperty("UtcTimestamp", DateTimeOffset.UtcNow);
        logEvent.AddPropertyIfAbsent(property);
    }
}
