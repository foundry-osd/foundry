using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace Foundry.Localization;

/// <summary>
/// Applies UI culture changes and resolves strings from a <see cref="ResourceManager" />.
/// </summary>
public class ResourceManagerLocalizationService : IResourceManagerLocalizationService, INotifyPropertyChanged
{
    private readonly ResourceManager resourceManager;
    private readonly LocalizedStrings strings;
    private CultureInfo currentCulture;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceManagerLocalizationService" /> class.
    /// </summary>
    /// <param name="resourceManager">Resource manager that owns the app string resources.</param>
    /// <param name="currentCulture">Initial UI culture.</param>
    public ResourceManagerLocalizationService(ResourceManager resourceManager, CultureInfo currentCulture)
    {
        ArgumentNullException.ThrowIfNull(resourceManager);
        ArgumentNullException.ThrowIfNull(currentCulture);

        this.resourceManager = resourceManager;
        this.currentCulture = currentCulture;
        strings = new LocalizedStrings(resourceManager, currentCulture);
    }

    /// <inheritdoc />
    public CultureInfo CurrentCulture
    {
        get => currentCulture;
        private set
        {
            if (Equals(currentCulture, value))
            {
                return;
            }

            currentCulture = value;
            CultureInfo.CurrentCulture = currentCulture;
            CultureInfo.CurrentUICulture = currentCulture;
            CultureInfo.DefaultThreadCurrentCulture = currentCulture;
            CultureInfo.DefaultThreadCurrentUICulture = currentCulture;
            strings.SetCulture(currentCulture);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentCulture)));
        }
    }

    /// <inheritdoc />
    public LocalizedStrings Strings => strings;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <inheritdoc />
    public event EventHandler<ApplicationLanguageChangedEventArgs>? LanguageChanged;

    /// <inheritdoc />
    public void SetCulture(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        if (Equals(CurrentCulture, culture))
        {
            return;
        }

        string oldLanguage = CurrentCulture.Name;
        CurrentCulture = culture;
        LanguageChanged?.Invoke(this, new ApplicationLanguageChangedEventArgs(oldLanguage, CurrentCulture.Name));
    }

    /// <inheritdoc />
    public string GetString(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        return resourceManager.GetString(key, CurrentCulture) ?? key;
    }

    /// <inheritdoc />
    public string Format(string key, params object[] args)
    {
        return string.Format(CurrentCulture, GetString(key), args);
    }

    /// <inheritdoc />
    public IReadOnlyList<SupportedCultureOption> CreateSupportedCultureOptions()
    {
        return SupportedCultureCatalog.CreateOptions(CurrentCulture, GetString);
    }
}
