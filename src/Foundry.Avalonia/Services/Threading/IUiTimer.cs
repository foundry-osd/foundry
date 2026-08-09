// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Avalonia.Services.Threading;

public interface IUiTimer : IDisposable
{
    bool IsEnabled { get; }

    event EventHandler? Tick;

    void Start();

    void Stop();
}
