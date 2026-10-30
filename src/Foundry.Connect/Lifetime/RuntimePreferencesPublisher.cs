// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using Foundry.Core.Services.Runtime;
using Foundry.Wpf.Localization;
using Foundry.Wpf.Theme;
using Microsoft.Extensions.Logging;

namespace Foundry.Connect.Lifetime;

/// <summary>
/// Hands the running theme and language to Foundry Deploy. Never throws.
/// </summary>
public interface IRuntimePreferencesPublisher
{
    /// <summary>
    /// Writes the theme and UI language currently shown by Foundry Connect to the hand-off file, best effort.
    /// </summary>
    void Publish();
}

/// <summary>
/// Publishes the theme and language through <see cref="RuntimePreferencesHandoff"/>.
/// </summary>
/// <remarks>
/// A write failure is logged as a warning and swallowed: the hand-off is a convenience and must never change the exit
/// outcome. The file and the log entries carry only the theme name and the culture name.
/// </remarks>
public sealed class RuntimePreferencesPublisher : IRuntimePreferencesPublisher
{
    private readonly IThemeService _theme;
    private readonly ILocalizationService _localization;
    private readonly string _path;
    private readonly ILogger<RuntimePreferencesPublisher> _logger;

    /// <summary>
    /// Initializes the publisher for the default hand-off file.
    /// </summary>
    /// <param name="theme">Source of the current theme.</param>
    /// <param name="localization">Source of the current UI culture.</param>
    /// <param name="logger">Logger.</param>
    public RuntimePreferencesPublisher(IThemeService theme, ILocalizationService localization, ILogger<RuntimePreferencesPublisher> logger)
        : this(theme, localization, RuntimePreferencesHandoff.DefaultPath, logger)
    {
    }

    internal RuntimePreferencesPublisher(IThemeService theme, ILocalizationService localization, string path, ILogger<RuntimePreferencesPublisher> logger)
    {
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _path = path ?? throw new ArgumentNullException(nameof(path));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public void Publish()
    {
        try
        {
            var preferences = new RuntimePreferences(ToRuntime(_theme.CurrentTheme), _localization.CurrentCulture.Name);
            RuntimePreferencesHandoff.Write(_path, preferences);
            _logger.LogInformation(
                "Theme and language handed to Foundry Deploy. Theme={Theme}, Culture={Culture}",
                preferences.Theme,
                preferences.CultureName);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // The exception is not attached: every log level is mirrored remotely and its message carries the file path.
            _logger.LogWarning(
                "Theme and language could not be handed to Foundry Deploy. FailureType={FailureType}, ErrorCode={ErrorCode}",
                exception.GetType().Name,
                exception.HResult);
        }
    }

    internal static RuntimeThemePreference ToRuntime(ThemeMode theme) => theme switch
    {
        ThemeMode.Light => RuntimeThemePreference.Light,
        ThemeMode.Dark => RuntimeThemePreference.Dark,
        _ => RuntimeThemePreference.System
    };
}
