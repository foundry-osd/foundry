using Foundry.Core.Localization;

namespace Foundry.Services.Localization;

public interface IApplicationLocalizationService
{
    string CurrentLanguage { get; }
    event EventHandler<ApplicationLanguageChangedEventArgs>? LanguageChanged;
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task SetLanguageAsync(string languageCode, CancellationToken cancellationToken = default);
    string GetString(string key);
    string FormatString(string key, params object[] args);
    IReadOnlyList<SupportedCultureOption> CreateSupportedLanguageOptions();
}
