// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Avalonia.Services.Threading;
using Foundry.Connect.Models;

namespace Foundry.Connect.Services.ApplicationLifetime;

public sealed class ApplicationLifetimeService : IApplicationLifetimeService
{
    private readonly IUiDispatcher _dispatcher;
    private readonly IApplicationExitHandler _exitHandler;

    public ApplicationLifetimeService(IUiDispatcher dispatcher, IApplicationExitHandler exitHandler)
    {
        _dispatcher = dispatcher;
        _exitHandler = exitHandler;
    }

    public bool IsExitRequested { get; private set; }

    public FoundryConnectExitCode ExitCode { get; private set; } = FoundryConnectExitCode.Success;

    public void Exit(FoundryConnectExitCode exitCode)
    {
        if (IsExitRequested)
        {
            return;
        }

        IsExitRequested = true;
        ExitCode = exitCode;

        if (_dispatcher.CheckAccess())
        {
            _exitHandler.Exit(exitCode);
            return;
        }

        _dispatcher.Post(() => _exitHandler.Exit(exitCode));
    }
}
