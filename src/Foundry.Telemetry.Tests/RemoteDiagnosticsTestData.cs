// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Serilog.Events;
using Serilog.Parsing;
using Foundry.Telemetry;

namespace Foundry.Telemetry.Tests;

internal static class RemoteDiagnosticsTestData
{
    public static RemoteDiagnosticsOptions EnabledOptions() => new(
        true,
        "https://eu.i.posthog.com",
        "phc_test",
        "install-1");

    public static RemoteDiagnosticsContext Context() => new(
        "foundry.deploy",
        "1.2.3",
        "release",
        "winpe",
        "x64",
        "en-US",
        "session-1",
        "foundry.deploy@1.2.3");

    public static LogEvent LogEvent(
        LogEventLevel level,
        string messageTemplate,
        Exception? exception = null,
        params (string Name, object Value)[] properties) => new(
            DateTimeOffset.UtcNow,
            level,
            exception,
            new MessageTemplateParser().Parse(messageTemplate),
            properties.Select(static property => new LogEventProperty(property.Name, new ScalarValue(property.Value))));
}
