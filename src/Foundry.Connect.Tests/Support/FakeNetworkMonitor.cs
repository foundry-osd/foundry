// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Network;

namespace Foundry.Connect.Tests.Support;

/// <summary>
/// <see cref="INetworkMonitor"/> that counts calls. <see cref="RunAsync"/> returns <see cref="RunResult"/>, completed
/// by default, so a view model that awaits it does not block.
/// </summary>
internal sealed class FakeNetworkMonitor : INetworkMonitor
{
    /// <summary>Gets the number of <see cref="RunAsync"/> calls.</summary>
    public int RunCalls { get; private set; }

    /// <summary>Gets the number of <see cref="RequestRefresh"/> calls.</summary>
    public int RefreshRequests { get; private set; }

    /// <summary>Gets or sets the task returned by <see cref="RunAsync"/>, for example a faulted monitor loop.</summary>
    public Task RunResult { get; set; } = Task.CompletedTask;

    /// <inheritdoc />
    public Task RunAsync(CancellationToken cancellationToken)
    {
        RunCalls++;
        return RunResult;
    }

    /// <inheritdoc />
    public void RequestRefresh() => RefreshRequests++;
}
