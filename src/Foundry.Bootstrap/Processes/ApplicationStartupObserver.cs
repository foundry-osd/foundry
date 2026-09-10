// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using Foundry.Core.Models.Runtime;

namespace Foundry.Bootstrap.Processes;

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

/// <summary>Limits startup to two minutes; acknowledged Connect interaction has no startup deadline.</summary>
internal sealed class ApplicationStartupObserver(TimeProvider? timeProvider = null,
    Func<CancellationToken, Task>? poll = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly Func<CancellationToken, Task> delay = poll ?? (token => Task.Delay(100, token));

    internal async Task<ApplicationLaunchResult> ObserveAsync(IObservedApplication process,
        Func<RuntimeStartupStatus?> readStatus, bool waitForCompletion, CancellationToken cancellationToken)
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
                if (waitForCompletion)
                {
                    await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                    last = readStatus() ?? last;
                    return Exited();
                }
                last = readStatus() ?? last;
                if (last.Stage == StartupStage.StartupFailed)
                {
                    return process.HasExited ? Exited() : new(false, FailureCategory: "startup_failed", LastStage: last.Stage);
                }
                // An exit observed before handoff takes precedence over an already written ready status.
                return process.HasExited ? Exited() : new(true, ReadinessConfirmed: true, LastStage: last.Stage);
            }
            if (clock.GetElapsedTime(started) >= TimeSpan.FromSeconds(120))
            {
                return new(false, FailureCategory: "readiness_timeout", LastStage: last?.Stage);
            }
            await delay(cancellationToken).ConfigureAwait(false);
        }

        ApplicationLaunchResult Exited() => new(waitForCompletion && process.ExitCode == 0, process.ExitCode,
            readyAcknowledged,
            waitForCompletion && process.ExitCode == 0 ? null : "child_exit", last?.Stage);
    }
}
