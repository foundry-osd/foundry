using Serilog.Core;
using Serilog.Events;

namespace Foundry.Logging;

internal sealed class UtcTimestampEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        LogEventProperty property = propertyFactory.CreateProperty("UtcTimestamp", DateTimeOffset.UtcNow);
        logEvent.AddPropertyIfAbsent(property);
    }
}
