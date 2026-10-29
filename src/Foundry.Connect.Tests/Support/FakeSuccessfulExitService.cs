// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Lifetime;

namespace Foundry.Connect.Tests.Support;

/// <summary>
/// <see cref="ISuccessfulExitService"/> that counts calls and returns a configured result, without any probe or exit.
/// </summary>
internal sealed class FakeSuccessfulExitService : ISuccessfulExitService
{
    /// <summary>Gets or sets the result returned by every call.</summary>
    public SuccessfulExitResult Result { get; set; } = SuccessfulExitResult.Exited;

    /// <summary>Gets the number of <see cref="TryCompleteAsync"/> calls.</summary>
    public int Calls { get; private set; }

    /// <inheritdoc />
    public Task<SuccessfulExitResult> TryCompleteAsync(CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(Result);
    }
}
