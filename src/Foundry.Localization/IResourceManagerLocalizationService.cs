// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;

namespace Foundry.Localization;

/// <summary>
/// Provides culture state and resource lookup for apps backed by <see cref="System.Resources.ResourceManager" />.
/// </summary>
public interface IResourceManagerLocalizationService
{
    /// <summary>
    /// Gets the currently active UI culture.
    /// </summary>
    CultureInfo CurrentCulture { get; }

    /// <summary>
    /// Gets the bindable localized string indexer used by WPF views.
    /// </summary>
    LocalizedStrings Strings { get; }

    /// <summary>
    /// Occurs after the active UI culture changes.
    /// </summary>
    event EventHandler<ApplicationLanguageChangedEventArgs>? LanguageChanged;

    /// <summary>
    /// Applies a new UI culture to the service and current thread.
    /// </summary>
    /// <param name="culture">Culture to apply.</param>
    void SetCulture(CultureInfo culture);

    /// <summary>
    /// Gets a localized string by key.
    /// </summary>
    /// <param name="key">Resource key.</param>
    /// <returns>The localized value, or the key when no resource exists.</returns>
    string GetString(string key);

    /// <summary>
    /// Gets and formats a localized string by key.
    /// </summary>
    /// <param name="key">Resource key.</param>
    /// <param name="args">Format arguments.</param>
    /// <returns>The formatted localized value.</returns>
    string Format(string key, params object[] args);

    /// <summary>
    /// Creates supported culture options for language selection UI.
    /// </summary>
    /// <returns>Supported culture options sorted for display.</returns>
    IReadOnlyList<SupportedCultureOption> CreateSupportedCultureOptions();
}
