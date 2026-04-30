using Foundry.Services.Settings;
using Serilog;

namespace Foundry.Services.Updates;

internal sealed class ApplicationUpdateService(
    IAppSettingsService appSettingsService,
    ILogger logger) : IApplicationUpdateService
{
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        logger.Information(
            "Update service initialized. CheckOnStartup={CheckOnStartup}, Channel={Channel}, FeedUrl={FeedUrl}",
            appSettingsService.Current.Updates.CheckOnStartup,
            appSettingsService.Current.Updates.Channel,
            appSettingsService.Current.Updates.FeedUrl);

        return Task.CompletedTask;
    }
}
