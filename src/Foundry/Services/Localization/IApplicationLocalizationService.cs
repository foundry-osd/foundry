namespace Foundry.Services.Localization;

public interface IApplicationLocalizationService
{
    string CurrentLanguage { get; }
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
