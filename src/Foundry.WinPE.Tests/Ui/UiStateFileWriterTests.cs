// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.WinPE.Tests.Support;
using Foundry.WinPE.Ui;
using Serilog;
using Serilog.Events;
using Xunit;

namespace Foundry.WinPE.Tests.Ui;

public sealed class UiStateFileWriterTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("FoundryUiStateWriter-").FullName;
    private readonly List<LogEvent> events = [];

    private string StatePath => Path.Combine(directory, "ui-state.json");

    [Fact]
    public void FailedDestructiveRecord_RemovesTheStateFileSoTheGuardianNeverOffersRelaunch()
    {
        UiStateFile.Write(StatePath, new UiState(UiPhase.Deploy, DestructiveStarted: false));
        int attempts = 0;
        var writer = CreateWriter((_, _) => { attempts++; throw new IOException("Disk full."); });

        writer.Write(new UiState(UiPhase.Deploy, DestructiveStarted: true));

        Assert.Equal(2, attempts);
        Assert.False(File.Exists(StatePath));
        Assert.Null(UiStateFile.Read(StatePath));
        Assert.Contains(events, entry => entry.Level == LogEventLevel.Warning);
        Assert.Contains(events, entry => entry.Level == LogEventLevel.Error);
    }

    [Fact]
    public void FailedRecord_IsRetriedOnce()
    {
        int attempts = 0;
        var writer = CreateWriter((path, state) =>
        {
            if (++attempts == 1) throw new IOException("Sharing violation.");
            UiStateFile.Write(path, state);
        });

        writer.Write(new UiState(UiPhase.Deploy, DestructiveStarted: true));

        Assert.Equal(2, attempts);
        Assert.Equal(new UiState(UiPhase.Deploy, DestructiveStarted: true), UiStateFile.Read(StatePath));
        Assert.Single(events, entry => entry.Level == LogEventLevel.Warning);
        Assert.DoesNotContain(events, entry => entry.Level == LogEventLevel.Error);
    }

    [Fact]
    public void FailedPhaseRecord_KeepsThePreviousState()
    {
        UiStateFile.Write(StatePath, new UiState(UiPhase.Connect, DestructiveStarted: false));
        var writer = CreateWriter((_, _) => throw new UnauthorizedAccessException());

        writer.Write(new UiState(UiPhase.System, DestructiveStarted: false));

        Assert.Equal(new UiState(UiPhase.Connect, DestructiveStarted: false), UiStateFile.Read(StatePath));
    }

    [Fact]
    public void FailedRemoval_IsLoggedAndNeverThrows()
    {
        var writer = CreateWriter((_, _) => throw new IOException(), _ => throw new UnauthorizedAccessException());

        writer.Write(new UiState(UiPhase.Deploy, DestructiveStarted: true));

        Assert.Equal(2, events.Count(entry => entry.Level == LogEventLevel.Error));
    }

    public void Dispose()
    {
        try { Directory.Delete(directory, recursive: true); }
        catch (IOException) { }
    }

    private UiStateFileWriter CreateWriter(Action<string, UiState> write, Action<string>? delete = null) =>
        new(StatePath, new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(new DelegatingSink(events.Add)).CreateLogger(),
            write, delete);
}
