// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Lifetime;
using Foundry.Core.Models.Runtime;

namespace Foundry.Connect.Tests.Support;

/// <summary>
/// <see cref="IApplicationLifetimeService"/> with the production contract and no shutdown: the first accepted exit
/// code wins and cancels <see cref="ExitRequested"/>; later calls return <see langword="false"/> and record nothing.
/// </summary>
internal sealed class FakeApplicationLifetime : IApplicationLifetimeService
{
    private readonly CancellationTokenSource _exitRequested = new();

    /// <summary>Gets the accepted exit codes; at most one, because the first writer wins.</summary>
    public List<ChildRuntimeExitCode> Codes { get; } = [];

    /// <inheritdoc />
    public ChildRuntimeExitCode? RequestedExitCode { get; private set; }

    /// <inheritdoc />
    public CancellationToken ExitRequested => _exitRequested.Token;

    /// <inheritdoc />
    public bool TryExit(ChildRuntimeExitCode exitCode)
    {
        if (RequestedExitCode is not null)
        {
            return false;
        }

        RequestedExitCode = exitCode;
        Codes.Add(exitCode);
        _exitRequested.Cancel();
        return true;
    }
}
