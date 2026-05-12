using System.Globalization;
using Foundry.Core.Localization;
using Foundry.Services.Settings;
using Microsoft.Windows.ApplicationModel.Resources;
using Microsoft.Windows.Globalization;
using Serilog;
using Windows.System.UserProfile;

namespace Foundry.Services.Localization;

/// <summary>
/// Applies the selected UI language and resolves localized WinUI resource strings.
/// </summary>
internal sealed class ApplicationLocalizationService(
    IAppSettingsService appSettingsService,
    ILogger logger) : IApplicationLocalizationService
{
    private readonly ILogger logger = logger.ForContext<ApplicationLocalizationService>();
    private ResourceManager? resourceManager;
    private ResourceContext? resourceContext;
    private string currentLanguage = SupportedCultureCatalog.DefaultCultureCode;

    /// <inheritdoc />
    public string CurrentLanguage => currentLanguage;

    /// <inheritdoc />
    public event EventHandler<ApplicationLanguageChangedEventArgs>? LanguageChanged;

    /// <inheritdoc />
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string configuredLanguage = appSettingsService.Current.Localization.Language;
        string validatedLanguage = appSettingsService.IsFirstRun
            ? SupportedCultureCatalog.MatchPreferredCulture(GetPreferredLanguageCodes())
            : SupportedCultureCatalog.ValidateOrDefault(configuredLanguage);

        if (!string.Equals(configuredLanguage, validatedLanguage, StringComparison.OrdinalIgnoreCase))
        {
            appSettingsService.Current.Localization.Language = validatedLanguage;
            try
            {
                appSettingsService.Save();
            }
            catch (Exception ex)
            {
                logger.Error(
                    ex,
                    "Failed to persist normalized application language. ConfiguredLanguage={ConfiguredLanguage}, NormalizedLanguage={NormalizedLanguage}",
                    configuredLanguage,
                    validatedLanguage);
                throw;
            }
        }

        ApplyLanguage(validatedLanguage);
        logger.Information("Localization initialized. Language={Language}", CurrentLanguage);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SetLanguageAsync(string languageCode, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string validatedLanguage = SupportedCultureCatalog.ValidateOrDefault(languageCode);
        if (!string.Equals(languageCode, validatedLanguage, StringComparison.OrdinalIgnoreCase))
        {
            logger.Warning(
                "Unsupported language requested; using fallback. RequestedLanguage={RequestedLanguage}, FallbackLanguage={FallbackLanguage}",
                languageCode,
                validatedLanguage);
        }

        if (string.Equals(currentLanguage, validatedLanguage, StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        string oldLanguage = currentLanguage;
        appSettingsService.Current.Localization.Language = validatedLanguage;
        try
        {
            appSettingsService.Save();
        }
        catch (Exception ex)
        {
            logger.Error(
                ex,
                "Failed to persist selected language. OldLanguage={OldLanguage}, NewLanguage={NewLanguage}",
                oldLanguage,
                validatedLanguage);
            throw;
        }

        ApplyLanguage(validatedLanguage);
        LanguageChanged?.Invoke(this, new ApplicationLanguageChangedEventArgs(oldLanguage, validatedLanguage));
        logger.Information("Application language changed. OldLanguage={OldLanguage}, NewLanguage={NewLanguage}", oldLanguage, validatedLanguage);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public string GetString(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        try
        {
            ResourceCandidate? candidate = TryGetResourceCandidate(key);
            string? value = candidate?.ValueAsString;
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }
        catch (Exception ex)
        {
            if (appSettingsService.Current.Diagnostics.DeveloperMode)
            {
                logger.Warning(ex, "Localized resource lookup failed. Key={Key}, Language={Language}", key, currentLanguage);
            }
        }

        if (appSettingsService.Current.Diagnostics.DeveloperMode)
        {
            logger.Warning("Localized resource key was not found. Key={Key}, Language={Language}", key, currentLanguage);
        }

        return key;
    }

    private ResourceCandidate? TryGetResourceCandidate(string key)
    {
        if (resourceManager is null || resourceContext is null)
        {
            return null;
        }

        string resourceKey = key.Replace('.', '/');
        ResourceMap resourceMap = resourceManager.MainResourceMap.GetSubtree("Resources");
        return resourceMap.TryGetValue(key, resourceContext)
            ?? resourceMap.TryGetValue(resourceKey, resourceContext);
    }

    /// <inheritdoc />
    public string FormatString(string key, params object[] args)
    {
        return string.Format(CultureInfo.CurrentCulture, GetString(key), args);
    }

    /// <inheritdoc />
    public IReadOnlyList<SupportedCultureOption> CreateSupportedLanguageOptions()
    {
        CultureInfo currentCulture = CultureInfo.GetCultureInfo(currentLanguage);
        return SupportedCultureCatalog.CreateOptions(currentCulture, GetString);
    }

    private void ApplyLanguage(string languageCode)
    {
        CultureInfo culture = CultureInfo.GetCultureInfo(languageCode);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        // WinUI resource lookup uses the primary language override plus an explicit ResourceContext qualifier.
        ApplicationLanguages.PrimaryLanguageOverride = languageCode;

        resourceManager ??= new ResourceManager();
        resourceContext ??= resourceManager.CreateResourceContext();
        resourceContext.QualifierValues["Language"] = languageCode;

        currentLanguage = languageCode;
        logger.Debug("Localization language applied. Language={Language}", CurrentLanguage);
    }

    private static IReadOnlyList<string?> GetPreferredLanguageCodes()
    {
        List<string?> languageCodes = [];

        try
        {
            languageCodes.AddRange(GlobalizationPreferences.Languages);
        }
        catch
        {
            // Fall back to CultureInfo below when WinRT user profile language APIs are unavailable.
        }

        languageCodes.Add(CultureInfo.CurrentUICulture.Name);
        return languageCodes;
    }
}
