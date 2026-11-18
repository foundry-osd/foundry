// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Runtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Connect.Lifetime;

/// <summary>
/// Accepts the first exit request, publishes it through <see cref="ExitRequested"/> and then completes
/// <see cref="Completion"/> with the exit code, which ends the Connect phase; competing later requests are logged and
/// ignored. It never shuts the WPF application down: the Foundry.WinPE UI process outlives the phase. Thread-safe.
/// </summary>
public sealed class ApplicationLifetimeService : IApplicationLifetimeService
{
    private const int NoExitRequested = -1;

    private readonly ILogger<ApplicationLifetimeService> _logger;
    private readonly CancellationTokenSource _exitRequested = new();
    private readonly TaskCompletionSource<ChildRuntimeExitCode> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _requestedExitCode = NoExitRequested;

    /// <summary>
    /// Initializes the exit authority.
    /// </summary>
    /// <param name="logger">The logger used for exit diagnostics.</param>
    public ApplicationLifetimeService(ILogger<ApplicationLifetimeService>? logger = null)
    {
        _logger = logger ?? NullLogger<ApplicationLifetimeService>.Instance;
    }

    /// <inheritdoc />
    public ChildRuntimeExitCode? RequestedExitCode
    {
        get
        {
            int code = Volatile.Read(ref _requestedExitCode);
            return code == NoExitRequested ? null : (ChildRuntimeExitCode)code;
        }
    }

    /// <inheritdoc />
    public CancellationToken ExitRequested => _exitRequested.Token;

    /// <summary>
    /// Gets a task that completes with the winning exit code once the first exit request is accepted. Its continuations run
    /// asynchronously, never on the thread that requested the exit.
    /// </summary>
    public Task<ChildRuntimeExitCode> Completion => _completion.Task;

    /// <inheritdoc />
    public bool TryExit(ChildRuntimeExitCode exitCode)
    {
        int previous = Interlocked.CompareExchange(ref _requestedExitCode, (int)exitCode, NoExitRequested);
        if (previous != NoExitRequested)
        {
            _logger.LogDebug(
                "Exit request ignored because an exit was already requested. IgnoredExitCode={IgnoredExitCode}, RequestedExitCode={RequestedExitCode}.",
                exitCode, (ChildRuntimeExitCode)previous);
            return false;
        }

        _logger.LogInformation("Foundry Connect exit requested. ExitCode={ExitCode}.", exitCode);
        try
        {
            _exitRequested.Cancel();
        }
        finally
        {
            // A failing cancellation callback must not prevent the completion that decides the phase exit code.
            _completion.TrySetResult(exitCode);
        }

        return true;
    }
}
