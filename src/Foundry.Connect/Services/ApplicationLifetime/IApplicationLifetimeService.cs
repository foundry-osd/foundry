using Foundry.Connect.Models;

namespace Foundry.Connect.Services.ApplicationLifetime;

public interface IApplicationLifetimeService
{
    bool IsExitRequested { get; }

    FoundryConnectExitCode ExitCode { get; }

    void Exit(FoundryConnectExitCode exitCode);
}
