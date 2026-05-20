using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace Foundry.Localization;

/// <summary>
/// Exposes localized strings through an indexer that can be refreshed by WPF bindings.
/// </summary>
public sealed class LocalizedStrings(ResourceManager resourceManager, CultureInfo currentCulture) : INotifyPropertyChanged
{
    private CultureInfo currentCulture = currentCulture;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Gets a localized string by resource key.
    /// </summary>
    /// <param name="key">Resource key.</param>
    /// <returns>The localized value, or the key when no resource exists.</returns>
    public string this[string key]
    {
        get => resourceManager.GetString(key, currentCulture) ?? key;
        set { }
    }

    /// <summary>
    /// Updates the culture used by the indexer and refreshes WPF indexer bindings.
    /// </summary>
    /// <param name="culture">Culture to use for subsequent lookups.</param>
    public void SetCulture(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        if (Equals(currentCulture, culture))
        {
            return;
        }

        currentCulture = culture;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }
}
