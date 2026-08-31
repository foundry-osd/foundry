// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Serilog.Core;
using Serilog.Events;

namespace Foundry.Utilities.Diagnostics;

internal sealed class SourceComponentEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        string component = "Application";
        if (logEvent.Properties.TryGetValue("SourceContext", out LogEventPropertyValue? value) &&
            value is ScalarValue { Value: string sourceContext } &&
            !string.IsNullOrWhiteSpace(sourceContext))
        {
            component = sourceContext[(sourceContext.LastIndexOf('.') + 1)..];
        }

        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("Component", component));
    }
}
