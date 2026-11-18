// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using Foundry.Core.Models.Runtime;

namespace Foundry.WinPE.Processes;

/// <summary>Observes a child without taking ownership of its termination.</summary>
internal interface IObservedApplication
{
    bool HasExited { get; }
    int ExitCode { get; }
    Task WaitForExitAsync(CancellationToken cancellationToken);
}

internal sealed class ObservedApplication(Process process) : IObservedApplication
{
    public bool HasExited => process.HasExited;
    public int ExitCode => process.ExitCode;
    public Task WaitForExitAsync(CancellationToken cancellationToken) => process.WaitForExitAsync(cancellationToken);
}

/// <summary>
/// Limits startup to two minutes; once the UI process reported <c>ui_ready</c> it has no deadline and is supervised until it
/// exits.
/// </summary>
/// <param name="timeProvider">The clock of the startup deadline.</param>
/// <param name="poll">The wait between startup status reads, 100 ms by default.</param>
/// <param name="supervisionPoll">The wait between supervision callbacks of a ready process, one second by default.</param>
internal sealed class ApplicationStartupObserver(TimeProvider? timeProvider = null,
    Func<CancellationToken, Task>? poll = null, Func<CancellationToken, Task>? supervisionPoll = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly Func<CancellationToken, Task> delay = poll ?? (token => Task.Delay(100, token));
    private readonly Func<CancellationToken, Task> supervisionDelay = supervisionPoll ?? (token => Task.Delay(1000, token));

    /// <summary>Observes <paramref name="process"/> until it fails to start, times out or exits.</summary>
    /// <param name="process">The UI process.</param>
    /// <param name="readStatus">Reads its startup status file.</param>
    /// <param name="cancellationToken">Stops observing; the process keeps running.</param>
    /// <param name="whileRunning">Called periodically, never before <c>ui_ready</c>, while the ready process runs.</param>
    internal async Task<ApplicationLaunchResult> ObserveAsync(IObservedApplication process,
        Func<RuntimeStartupStatus?> readStatus, CancellationToken cancellationToken, Action? whileRunning = null)
    {
        long started = clock.GetTimestamp();
        RuntimeStartupStatus? last = null;
        bool readyAcknowledged = false;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            last = readStatus() ?? last;
            readyAcknowledged |= last?.Stage == StartupStage.UiReady;
            if (process.HasExited) { return Exited(); }
            if (last?.Stage == StartupStage.StartupFailed)
            {
                return new(false, FailureCategory: "startup_failed", LastStage: last.Stage);
            }
            if (last?.Stage == StartupStage.UiReady)
            {
                // One exit wait for the whole supervision, polled alongside the supervision delay.
                Task exited = process.WaitForExitAsync(cancellationToken);
                while (!exited.IsCompleted)
                {
                    whileRunning?.Invoke();
                    await Task.WhenAny(exited, supervisionDelay(cancellationToken)).ConfigureAwait(false);
                }

                await exited.ConfigureAwait(false);
                last = readStatus() ?? last;
                return Exited();
            }
            if (clock.GetElapsedTime(started) >= TimeSpan.FromSeconds(120))
            {
                return new(false, FailureCategory: "readiness_timeout", LastStage: last?.Stage);
            }
            await delay(cancellationToken).ConfigureAwait(false);
        }

        ApplicationLaunchResult Exited() => new(process.ExitCode == 0, process.ExitCode, readyAcknowledged,
            process.ExitCode == 0 ? null : "child_exit", last?.Stage);
    }
}
