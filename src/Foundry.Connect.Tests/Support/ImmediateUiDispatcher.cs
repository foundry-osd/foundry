// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.ViewModels;

namespace Foundry.Connect.Tests.Support;

/// <summary>
/// Runs every action inline on the calling thread, so view-model tests are deterministic and single-threaded.
/// </summary>
internal sealed class ImmediateUiDispatcher : IUiDispatcher
{
    /// <inheritdoc />
    public void Run(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        action();
    }
}
