// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Utilities.Diagnostics;
using Foundry.WinPE.Tests.Support;
using Foundry.WinPE.Ui;
using Serilog;
using Serilog.Events;
using Xunit;

namespace Foundry.WinPE.Tests.Ui;

/// <summary>Serializes the tests that replace the process-wide <see cref="Log.Logger"/>.</summary>
[CollectionDefinition(nameof(GlobalLoggerCollection), DisableParallelization = true)]
public sealed class GlobalLoggerCollection;

[Collection(nameof(GlobalLoggerCollection))]
public sealed class UiHostLogTests
{
    [Fact]
    public void HostLogger_KeepsTheUiProcessApplicationWhileAPhaseLoggerIsCurrent()
    {
        var events = new List<LogEvent>();
        ILogger previous = Log.Logger;
        try
        {
            // A phase logger, as HostedPhaseScope makes current: it enriches entries with the phase's application.
            Log.Logger = FoundryLogConfiguration.CreateDebugLogger("Foundry.Connect", "SESSION_1", LogEventLevel.Verbose,
                additionalSink: new DelegatingSink(events.Add));

            UiHostLog.CreateLogger(typeof(WinPeUiSession)).Information("Host entry");
            Log.Logger.Information("Phase entry");

            Assert.Equal(["\"Foundry.WinPE\"", "\"Foundry.Connect\""], events.Select(item => item.Properties["Application"].ToString()));
        }
        finally
        {
            (Log.Logger as IDisposable)?.Dispose();
            Log.Logger = previous;
        }
    }
}
