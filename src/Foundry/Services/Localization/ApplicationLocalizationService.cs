using System.Globalization;
using Foundry.Core.Localization;
using Foundry.Services.Settings;
using Microsoft.Windows.ApplicationModel.Resources;
using Microsoft.Windows.Globalization;
using Serilog;

namespace Foundry.Services.Localization;

internal sealed class ApplicationLocalizationService(
    IAppSettingsService appSettingsService,
    ILogger logger) : IApplicationLocalizationService
{
    private ResourceManager? resourceManager;
    private ResourceContext? resourceContext;
    private string currentLanguage = SupportedCultureCatalog.DefaultCultureCode;

    public string CurrentLanguage => currentLanguage;

    public event EventHandler<ApplicationLanguageChangedEventArgs>? LanguageChanged;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string configuredLanguage = appSettingsService.Current.Localization.Language;
        string validatedLanguage = SupportedCultureCatalog.ValidateOrDefault(configuredLanguage);
        if (!string.Equals(configuredLanguage, validatedLanguage, StringComparison.OrdinalIgnoreCase))
        {
            appSettingsService.Current.Localization.Language = validatedLanguage;
            appSettingsService.Save();
        }

        ApplyLanguage(validatedLanguage);
        return Task.CompletedTask;
    }

    public Task SetLanguageAsync(string languageCode, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string validatedLanguage = SupportedCultureCatalog.ValidateOrDefault(languageCode);
        if (string.Equals(currentLanguage, validatedLanguage, StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        string oldLanguage = currentLanguage;
        ApplyLanguage(validatedLanguage);

        appSettingsService.Current.Localization.Language = validatedLanguage;
        appSettingsService.Save();

        LanguageChanged?.Invoke(this, new ApplicationLanguageChangedEventArgs(oldLanguage, validatedLanguage));
        return Task.CompletedTask;
    }

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

    public string FormatString(string key, params object[] args)
    {
        return string.Format(CultureInfo.CurrentCulture, GetString(key), args);
    }

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

        ApplicationLanguages.PrimaryLanguageOverride = languageCode;

        resourceManager ??= new ResourceManager();
        resourceContext ??= resourceManager.CreateResourceContext();
        resourceContext.QualifierValues["Language"] = languageCode;

        currentLanguage = languageCode;
        logger.Information("Localization initialized. Language={Language}", CurrentLanguage);
    }
}
