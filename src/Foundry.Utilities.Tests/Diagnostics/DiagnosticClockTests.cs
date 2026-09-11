// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Utilities.Diagnostics;
using Serilog.Events;
using Serilog.Parsing;

namespace Foundry.Utilities.Tests.Diagnostics;

public sealed class DiagnosticClockTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("false", false)]
    [InlineData("invalid", false)]
    [InlineData("true", true)]
    public void RuntimeUsesOnlyExplicitInheritedSynchronization(string? inherited, bool expected)
    {
        var clock = new DiagnosticClock();
        clock.InitializeRuntime(inherited);
        LogEvent entry = CreateEvent();
        clock.Enrich(entry);
        Assert.Equal(expected, ((ScalarValue)entry.Properties[DiagnosticClock.SynchronizedProperty]).Value);
        Assert.Equal(expected ? "event" : "ingestion", ((ScalarValue)entry.Properties[DiagnosticClock.TimestampSourceProperty]).Value);
    }

    [Fact]
    public void LaterSynchronizationCannotReclassifyAnEarlierEvent()
    {
        var clock = new DiagnosticClock();
        clock.InitializeRuntime(null);
        LogEvent earlier = CreateEvent();
        DateTimeOffset originalTime = earlier.Timestamp;
        clock.Enrich(earlier);
        clock.MarkSynchronized();
        clock.Enrich(earlier);
        LogEvent normalized = LogEventNormalizer.Normalize(earlier);
        LogEvent later = CreateEvent();
        clock.Enrich(later);

        Assert.Equal(originalTime, normalized.Timestamp);
        Assert.Equal(false, ((ScalarValue)normalized.Properties[DiagnosticClock.SynchronizedProperty]).Value);
        Assert.Equal(originalTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            ((ScalarValue)normalized.Properties[DiagnosticClock.OriginalTimestampProperty]).Value);
        Assert.Equal(true, ((ScalarValue)later.Properties[DiagnosticClock.SynchronizedProperty]).Value);
    }

    [Fact]
    public void DesktopDefaultDoesNotOverrideEventTimestampPolicy()
    {
        var clock = new DiagnosticClock();
        LogEvent entry = CreateEvent();
        clock.Enrich(entry);
        Assert.Null(clock.IsSynchronized);
        Assert.DoesNotContain(DiagnosticClock.SynchronizedProperty, entry.Properties.Keys);
    }

    private static LogEvent CreateEvent() => new(DateTimeOffset.UtcNow.AddHours(10), LogEventLevel.Debug, null,
        new MessageTemplateParser().Parse("Connecting"), []);
}
