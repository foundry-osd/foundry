// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Models;

namespace Foundry.Connect.Services.ApplicationLifetime;

public interface IApplicationLifetimeService
{
    bool IsExitRequested { get; }

    FoundryConnectExitCode ExitCode { get; }

    void Exit(FoundryConnectExitCode exitCode);
}
