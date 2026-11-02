// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Wpf.Threading;

namespace Foundry.AutopilotInteractive.ViewModels;

/// <summary>
/// <see cref="IProgress{T}"/> that delivers each report on the UI thread, in report order.
/// </summary>
/// <remarks>
/// <see cref="Progress{T}"/> is not used because it depends on the synchronization context of the thread that creates it.
/// </remarks>
/// <typeparam name="T">Type of the reported value.</typeparam>
/// <param name="dispatcher">Marshals each report to the UI thread.</param>
/// <param name="handler">Receives each report on the UI thread.</param>
internal sealed class DispatcherProgress<T>(IUiDispatcher dispatcher, Action<T> handler) : IProgress<T>
{
    /// <inheritdoc />
    public void Report(T value) => dispatcher.Run(() => handler(value));
}
