// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Runtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Connect.Lifetime;

/// <summary>
/// Accepts the first exit request, publishes it through <see cref="ExitRequested"/> and then runs the injected
/// shutdown action with the exit code; competing later requests are logged and ignored. Thread-safe.
/// </summary>
public sealed class ApplicationLifetimeService : IApplicationLifetimeService
{
    private const int NoExitRequested = -1;

    private readonly Action<int> _shutdown;
    private readonly ILogger<ApplicationLifetimeService> _logger;
    private readonly CancellationTokenSource _exitRequested = new();
    private int _requestedExitCode = NoExitRequested;

    /// <summary>
    /// Initializes the exit authority.
    /// </summary>
    /// <param name="shutdown">Shuts the application down with the given process exit code; called once, on the thread of the winning request.</param>
    /// <param name="logger">The logger used for exit diagnostics.</param>
    public ApplicationLifetimeService(Action<int> shutdown, ILogger<ApplicationLifetimeService>? logger = null)
    {
        _shutdown = shutdown;
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

        _logger.LogInformation("Application exit requested. ExitCode={ExitCode}.", exitCode);
        try
        {
            _exitRequested.Cancel();
        }
        finally
        {
            // A failing cancellation callback must not prevent the shutdown that decides the process exit code.
            _shutdown((int)exitCode);
        }

        return true;
    }
}
