// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Connect.ViewModels;

/// <summary>
/// Runs work on the UI thread: inline when already on it, queued otherwise. View models marshal every callback that
/// can arrive on another thread (coordinator state changes, timers) through it.
/// </summary>
public interface IUiDispatcher
{
    /// <summary>
    /// Runs <paramref name="action"/> on the UI thread. It never waits for a queued action to finish.
    /// </summary>
    /// <param name="action">The work to run.</param>
    void Run(Action action);
}
