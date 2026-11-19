// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Security;
using Foundry.Core.Services.Runtime;

namespace Foundry.Connect.Tests.Support;

/// <summary>
/// Scriptable <see cref="IConnectUnlockService"/> for view-model tests. Every call is recorded, runs
/// <see cref="OnUnlock"/> synchronously (as the first prompt runs inside the real call) and returns <see cref="Result"/>,
/// or the result of <see cref="Gate"/> once the test completes it. <see cref="IsPromptOpen"/> is set for the whole call.
/// </summary>
internal sealed class FakeUnlockService : IConnectUnlockService
{
    /// <inheritdoc />
    public bool IsWiredUnlockRequired { get; set; }

    /// <inheritdoc />
    public bool IsWifiUnlockRequired { get; set; }

    /// <inheritdoc />
    public bool IsPromptOpen { get; private set; }

    /// <summary>Gets or sets the result of every <see cref="UnlockAsync"/> call when no gate is set.</summary>
    public ConnectUnlockResult Result { get; set; } = ConnectUnlockResult.NotRequired;

    /// <summary>Gets or sets a gate that keeps the simulated prompt open until the test completes it.</summary>
    public TaskCompletionSource<ConnectUnlockResult>? Gate { get; set; }

    /// <summary>Gets the trigger of every <see cref="UnlockAsync"/> call.</summary>
    public List<ConnectUnlockTrigger> Calls { get; } = [];

    /// <summary>Gets or sets work run inside <see cref="UnlockAsync"/>, while the simulated prompt is open.</summary>
    public Action? OnUnlock { get; set; }

    /// <inheritdoc />
    public async Task<ConnectUnlockResult> UnlockAsync(ConnectUnlockTrigger trigger, CancellationToken cancellationToken)
    {
        Calls.Add(trigger);
        IsPromptOpen = true;
        try
        {
            OnUnlock?.Invoke();
            return Gate is null ? Result : await Gate.Task;
        }
        finally
        {
            IsPromptOpen = false;
        }
    }

    /// <inheritdoc />
    public DeploymentKeyTransfer? TakeDeploymentKey() => null;
}
