// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Avalonia.Services.Threading;

public interface IUiTimerFactory
{
    IUiTimer Create(TimeSpan interval);
}
