// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Lifetime;

namespace Foundry.Connect.Tests.Support;

/// <summary>
/// <see cref="ISuccessfulExitService"/> that counts calls and returns a configured result, without any probe or exit.
/// A <see cref="Gate"/> holds every call open until the test completes it, faults it or cancels it.
/// </summary>
internal sealed class FakeSuccessfulExitService : ISuccessfulExitService
{
    /// <summary>Gets or sets the result returned by every call when no gate is set.</summary>
    public SuccessfulExitResult Result { get; set; } = SuccessfulExitResult.Exited;

    /// <summary>Gets or sets a gate whose task every call returns, to test the in-flight window or a throwing exit.</summary>
    public TaskCompletionSource<SuccessfulExitResult>? Gate { get; set; }

    /// <summary>Gets the number of <see cref="TryCompleteAsync"/> calls.</summary>
    public int Calls { get; private set; }

    /// <inheritdoc />
    public Task<SuccessfulExitResult> TryCompleteAsync(CancellationToken cancellationToken)
    {
        Calls++;
        return Gate?.Task ?? Task.FromResult(Result);
    }
}
