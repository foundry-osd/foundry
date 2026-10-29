// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Models;
using Foundry.Core.Models.Runtime;

namespace Foundry.Connect.Services.ApplicationLifetime;

public interface IApplicationLifetimeService
{
    bool IsExitRequested { get; }

    ChildRuntimeExitCode ExitCode { get; }

    void Exit(ChildRuntimeExitCode exitCode);
}
