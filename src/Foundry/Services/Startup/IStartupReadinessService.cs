namespace Foundry.Services.Startup;

public interface IStartupReadinessService
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
