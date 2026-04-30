namespace Foundry.Services.ApplicationUpdate;

public interface IApplicationUpdateService
{
    Task CheckForUpdatesAsync(CancellationToken cancellationToken = default);

    Task CheckForUpdatesOnStartupAsync(CancellationToken cancellationToken = default);
}
