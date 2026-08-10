// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Foundry.Connect.Models;
using Foundry.Connect.Services.ApplicationLifetime;

namespace Foundry.Connect.Platform;

internal sealed class AvaloniaApplicationExitHandler : IApplicationExitHandler
{
    public void Exit(FoundryConnectExitCode exitCode)
    {
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown((int)exitCode);
    }
}
