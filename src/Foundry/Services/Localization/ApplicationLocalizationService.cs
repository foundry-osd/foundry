using Foundry.Services.Settings;
using Serilog;

namespace Foundry.Services.Localization;

internal sealed class ApplicationLocalizationService(
    IAppSettingsService appSettingsService,
    ILogger logger) : IApplicationLocalizationService
{
    public string CurrentLanguage => appSettingsService.Current.Localization.Language;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        logger.Information("Localization initialized. Language={Language}", CurrentLanguage);
        return Task.CompletedTask;
    }
}
