using System.Windows;
using Foundry.Connect.Models;

namespace Foundry.Connect.Services.ApplicationLifetime;

public sealed class ApplicationLifetimeService : IApplicationLifetimeService
{
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

        Application? app = Application.Current;
        if (app is null)
        {
            return;
        }

        if (app.Dispatcher.CheckAccess())
        {
            app.Shutdown((int)exitCode);
            return;
        }

        _ = app.Dispatcher.InvokeAsync(() => app.Shutdown((int)exitCode));
    }
}
