using Foundry.Core.Localization;

namespace Foundry.Services.Localization;

/// <summary>
/// Provides application language state, resource lookup, and culture option creation.
/// </summary>
public interface IApplicationLocalizationService
{
    /// <summary>
    /// Gets the currently active application language code.
    /// </summary>
    string CurrentLanguage { get; }

    /// <summary>
    /// Occurs after the application language changes.
    /// </summary>
    event EventHandler<ApplicationLanguageChangedEventArgs>? LanguageChanged;

    /// <summary>
    /// Initializes localization from persisted settings.
    /// </summary>
    /// <param name="cancellationToken">Token that cancels initialization.</param>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes the active application language and persists the preference.
    /// </summary>
    /// <param name="languageCode">Culture code to activate.</param>
    /// <param name="cancellationToken">Token that cancels the change.</param>
    Task SetLanguageAsync(string languageCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a localized string by resource key.
    /// </summary>
    /// <param name="key">Resource key.</param>
    /// <returns>The localized string, or a fallback when the key is missing.</returns>
    string GetString(string key);

    /// <summary>
    /// Gets and formats a localized string by resource key.
    /// </summary>
    /// <param name="key">Resource key.</param>
    /// <param name="args">Format arguments.</param>
    /// <returns>The formatted localized string.</returns>
    string FormatString(string key, params object[] args);

    /// <summary>
    /// Creates the supported language option list used by settings UI.
    /// </summary>
    /// <returns>Supported culture options sorted for display.</returns>
    IReadOnlyList<SupportedCultureOption> CreateSupportedLanguageOptions();
}
