namespace Foundry.Services.Updates;

public interface IApplicationUpdateService
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
