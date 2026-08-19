// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Configuration;
using Microsoft.UI.Xaml.Media;

namespace Foundry.ViewModels;

/// <summary>
/// Represents one configuration summary row shown on the media Start page.
/// </summary>
public sealed class StartConfigurationOverviewItemViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="StartConfigurationOverviewItemViewModel"/> class.
    /// </summary>
    public StartConfigurationOverviewItemViewModel(
        string title,
        string description,
        string status,
        string glyph,
        string glyphForegroundBrushKey,
        ConfigurationOverviewState state,
        ConfigurationNavigationTarget navigationTarget,
        string actionText,
        string actionAutomationName)
    {
        Title = title;
        Description = description;
        Status = status;
        Glyph = glyph;
        GlyphForegroundBrushKey = glyphForegroundBrushKey;
        State = state;
        NavigationTarget = navigationTarget;
        ActionText = actionText;
        ActionAutomationName = actionAutomationName;
    }

    /// <summary>Gets the configuration area title.</summary>
    public string Title { get; }

    /// <summary>Gets the effective configuration summary.</summary>
    public string Description { get; }

    /// <summary>Gets the localized configuration state.</summary>
    public string Status { get; }

    /// <summary>Gets the state glyph.</summary>
    public string Glyph { get; }

    /// <summary>Gets the theme-aware glyph brush.</summary>
    public Brush GlyphForeground => (Brush)Application.Current.Resources[GlyphForegroundBrushKey];

    private string GlyphForegroundBrushKey { get; }

    /// <summary>Gets the evaluated configuration state.</summary>
    public ConfigurationOverviewState State { get; }

    /// <summary>Gets the page opened by the attention action.</summary>
    public ConfigurationNavigationTarget NavigationTarget { get; }

    /// <summary>Gets the attention action label.</summary>
    public string ActionText { get; }

    /// <summary>Gets the accessible attention action name.</summary>
    public string ActionAutomationName { get; }

    /// <summary>Gets the attention action visibility.</summary>
    public Visibility ActionVisibility => NavigationTarget == ConfigurationNavigationTarget.None
        ? Visibility.Collapsed
        : Visibility.Visible;
}
