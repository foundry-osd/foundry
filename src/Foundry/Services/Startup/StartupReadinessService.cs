using Foundry.Services.Settings;
using Foundry.Services.Shell;
using Foundry.Services.Updates;
using Serilog;

namespace Foundry.Services.Startup;

internal sealed class StartupReadinessService(
    IAppSettingsService appSettingsService,
    IApplicationUpdateService updateService,
    IShellNavigationGuardService shellNavigationGuardService,
    ILogger logger) : IStartupReadinessService
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        logger.Information(
            "Startup readiness initialization started. CheckOnStartup={CheckOnStartup}, UpdateChannel={UpdateChannel}",
            appSettingsService.Current.Updates.CheckOnStartup,
            appSettingsService.Current.Updates.Channel);

        shellNavigationGuardService.SetState(ShellNavigationState.Ready);

        await updateService.InitializeAsync(cancellationToken);

        logger.Information("Startup readiness initialized. ShellNavigationState={ShellNavigationState}", shellNavigationGuardService.State);
    }
}
