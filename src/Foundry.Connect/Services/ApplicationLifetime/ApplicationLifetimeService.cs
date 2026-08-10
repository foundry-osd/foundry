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
    private readonly object _exitLock = new();
    private bool _isExitRequested;
    private FoundryConnectExitCode _exitCode = FoundryConnectExitCode.Success;

    public ApplicationLifetimeService(IUiDispatcher dispatcher, IApplicationExitHandler exitHandler)
    {
        _dispatcher = dispatcher;
        _exitHandler = exitHandler;
    }

    public bool IsExitRequested
    {
        get
        {
            lock (_exitLock)
            {
                return _isExitRequested;
            }
        }
    }

    public FoundryConnectExitCode ExitCode
    {
        get
        {
            lock (_exitLock)
            {
                return _exitCode;
            }
        }
    }

    public void Exit(FoundryConnectExitCode exitCode)
    {
        lock (_exitLock)
        {
            if (_isExitRequested)
            {
                return;
            }

            _exitCode = exitCode;
            _isExitRequested = true;
        }

        if (_dispatcher.CheckAccess())
        {
            _exitHandler.Exit(exitCode);
            return;
        }

        _dispatcher.Post(() => _exitHandler.Exit(exitCode));
    }
}
