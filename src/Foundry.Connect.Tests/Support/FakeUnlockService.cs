// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Security;

namespace Foundry.Connect.Tests.Support;

/// <summary>
/// Scriptable <see cref="IConnectUnlockService"/> for view-model tests. Every call is recorded, runs
/// <see cref="OnUnlock"/> synchronously (as the first prompt runs inside the real call) and returns <see cref="Result"/>.
/// </summary>
internal sealed class FakeUnlockService : IConnectUnlockService
{
    /// <inheritdoc />
    public bool IsWiredUnlockRequired { get; set; }

    /// <inheritdoc />
    public bool IsWifiUnlockRequired { get; set; }

    /// <summary>Gets or sets the result of every <see cref="UnlockAsync"/> call.</summary>
    public ConnectUnlockResult Result { get; set; } = ConnectUnlockResult.NotRequired;

    /// <summary>Gets the trigger of every <see cref="UnlockAsync"/> call.</summary>
    public List<ConnectUnlockTrigger> Calls { get; } = [];

    /// <summary>Gets or sets work run inside <see cref="UnlockAsync"/>, while the simulated prompt is open.</summary>
    public Action? OnUnlock { get; set; }

    /// <inheritdoc />
    public Task<ConnectUnlockResult> UnlockAsync(ConnectUnlockTrigger trigger, CancellationToken cancellationToken)
    {
        Calls.Add(trigger);
        OnUnlock?.Invoke();
        return Task.FromResult(Result);
    }
}
