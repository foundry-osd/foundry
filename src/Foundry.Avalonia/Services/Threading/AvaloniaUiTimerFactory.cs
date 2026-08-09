// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Avalonia.Services.Threading;

public sealed class AvaloniaUiTimerFactory : IUiTimerFactory
{
    public IUiTimer Create(TimeSpan interval) => new AvaloniaUiTimer(interval);
}
