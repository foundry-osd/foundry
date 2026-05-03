using Foundry.Services.Adk;
using Foundry.Services.Settings;
using Foundry.Services.Shell;
using Foundry.Services.Updates;
using Serilog;

namespace Foundry.Services.Startup;

internal sealed class StartupReadinessService(
    IAppSettingsService appSettingsService,
    IApplicationUpdateService updateService,
    IAdkService adkService,
    IShellNavigationGuardService shellNavigationGuardService,
    ILogger logger) : IStartupReadinessService
{
    private readonly ILogger logger = logger.ForContext<StartupReadinessService>();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            logger.Information(
                "Startup readiness initialization started. CheckOnStartup={CheckOnStartup}, UpdateChannel={UpdateChannel}",
                appSettingsService.Current.Updates.CheckOnStartup,
                appSettingsService.Current.Updates.Channel);
            logger.Debug(
                "Startup readiness diagnostics. LogDirectoryPath={LogDirectoryPath}, SettingsPath={SettingsPath}, UpdateChannel={UpdateChannel}",
                Constants.LogDirectoryPath,
                Constants.AppSettingsPath,
                appSettingsService.Current.Updates.Channel);

            var adkStatus = await adkService.RefreshStatusAsync(cancellationToken);
            shellNavigationGuardService.SetState(adkStatus.CanCreateMedia ? ShellNavigationState.Ready : ShellNavigationState.AdkBlocked);

            await updateService.InitializeAsync(cancellationToken);

            logger.Information("Startup readiness initialized. ShellNavigationState={ShellNavigationState}", shellNavigationGuardService.State);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Startup readiness initialization failed. ShellNavigationState={ShellNavigationState}", shellNavigationGuardService.State);
            throw;
        }
    }
}
