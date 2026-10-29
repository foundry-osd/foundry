// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Network.Native;
using Microsoft.Extensions.Logging;

namespace Foundry.Connect.Tests.Network;

public sealed class WlanClientSnapshotLoggingTests
{
    // Snapshots are polled every refresh; a missing runtime must not log the same failure each time.
    [Fact]
    public void ReportSnapshotFailure_LogsOnlyWhenTheFailureChanges()
    {
        RecordingLogger logger = new();
        WlanClient client = new(TimeProvider.System, logger);

        client.ReportSnapshotFailure(null);
        client.ReportSnapshotFailure("WlanOpenHandle:1062");
        client.ReportSnapshotFailure("WlanOpenHandle:1062");
        client.ReportSnapshotFailure("DllNotFoundException:-2146233052");
        client.ReportSnapshotFailure("DllNotFoundException:-2146233052");
        client.ReportSnapshotFailure(null);
        client.ReportSnapshotFailure(null);

        Assert.Equal(
            [
                "WLAN snapshot read failed. SnapshotFailure=WlanOpenHandle:1062",
                "WLAN snapshot read failed. SnapshotFailure=DllNotFoundException:-2146233052",
                "WLAN snapshot read succeeded again. PreviousSnapshotFailure=DllNotFoundException:-2146233052"
            ],
            logger.Messages);
        Assert.All(logger.Levels, static level => Assert.Equal(LogLevel.Debug, level));
        Assert.Equal(0, logger.ExceptionCount);
    }

    private sealed class RecordingLogger : ILogger<WlanClient>
    {
        public List<string> Messages { get; } = [];

        public List<LogLevel> Levels { get; } = [];

        public int ExceptionCount { get; private set; }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
            Levels.Add(logLevel);
            if (exception is not null)
            {
                ExceptionCount++;
            }
        }
    }
}
