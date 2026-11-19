// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Services.Network;

namespace Foundry.Deploy.Tests;

/// <summary>
/// Shows a scripted page and then, like the real navigator, runs the return refresh once the page is no longer open.
/// </summary>
internal sealed class CallbackNetworkPageNavigator : INetworkPageNavigator
{
    public bool IsAvailable => true;

    public bool IsOpen { get; private set; }

    public bool HasEnded { get; private set; }

    public int Opens { get; private set; }

    /// <summary>Runs while the page is shown.</summary>
    public Action? OnPage { get; set; }

    /// <summary>
    /// The number of the open (from 1) during which the phase ends: that page is cancelled and its open returns
    /// <see langword="false"/> without a refresh, as the real navigator does.
    /// </summary>
    public int? EndPhaseAtOpen { get; set; }

    /// <summary>The return refresh, usually the view model's.</summary>
    public Func<CancellationToken, Task>? Refresh { get; set; }

    public async Task<bool> OpenAsync(CancellationToken cancellationToken)
    {
        if (IsOpen || HasEnded)
        {
            return false;
        }

        Opens++;
        IsOpen = true;
        OnPage?.Invoke();
        IsOpen = false;
        if (Opens == EndPhaseAtOpen)
        {
            HasEnded = true;
            return false;
        }

        if (Refresh is not null)
        {
            await Refresh(cancellationToken);
        }

        return true;
    }

    public Task CancelAndWaitAsync() => Task.CompletedTask;
}
