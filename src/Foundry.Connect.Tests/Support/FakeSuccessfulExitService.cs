// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Lifetime;

namespace Foundry.Connect.Tests.Support;

/// <summary>
/// <see cref="ISuccessfulExitService"/> that counts the calls of both exits and returns a configured result, without any
/// probe or exit.
/// A <see cref="Gate"/> holds every call open until the test completes it, faults it or cancels it.
/// </summary>
internal sealed class FakeSuccessfulExitService : ISuccessfulExitService
{
    /// <summary>Gets or sets the result returned by every call when no gate is set and <see cref="Results"/> is empty.</summary>
    public SuccessfulExitResult Result { get; set; } = SuccessfulExitResult.Exited;

    /// <summary>Gets results handed out one per call before <see cref="Result"/> applies.</summary>
    public Queue<SuccessfulExitResult> Results { get; } = new();

    /// <summary>Gets or sets a gate whose task every call returns, to test the in-flight window or a throwing exit.</summary>
    public TaskCompletionSource<SuccessfulExitResult>? Gate { get; set; }

    /// <summary>Gets the number of <see cref="TryCompleteAsync"/> calls.</summary>
    public int Calls => ConnectSkipped.Count;

    /// <summary>Gets the <c>connectSkipped</c> argument of every call.</summary>
    public List<bool> ConnectSkipped { get; } = [];

    /// <summary>Gets the number of <see cref="TryReturnToDeploymentAsync"/> calls.</summary>
    public int ReturnCalls { get; private set; }

    /// <inheritdoc />
    public Task<SuccessfulExitResult> TryReturnToDeploymentAsync(CancellationToken cancellationToken)
    {
        ReturnCalls++;
        return Gate?.Task ?? Task.FromResult(Results.Count > 0 ? Results.Dequeue() : Result);
    }

    /// <inheritdoc />
    public Task<SuccessfulExitResult> TryCompleteAsync(bool connectSkipped, CancellationToken cancellationToken)
    {
        ConnectSkipped.Add(connectSkipped);
        return Gate?.Task ?? Task.FromResult(Results.Count > 0 ? Results.Dequeue() : Result);
    }
}
