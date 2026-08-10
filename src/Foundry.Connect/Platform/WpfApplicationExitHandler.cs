// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Windows;
using Foundry.Connect.Models;
using Foundry.Connect.Services.ApplicationLifetime;

namespace Foundry.Connect.Platform;

internal sealed class WpfApplicationExitHandler : IApplicationExitHandler
{
    public void Exit(FoundryConnectExitCode exitCode)
    {
        Application.Current?.Shutdown((int)exitCode);
    }
}
