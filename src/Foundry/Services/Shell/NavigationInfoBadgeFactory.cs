// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Services.Shell;

/// <summary>
/// Creates icon-only information badges for shell navigation items.
/// </summary>
internal static class NavigationInfoBadgeFactory
{
    /// <summary>The Segoe Fluent Icons "Download" glyph of the <see cref="NavigationInfoBadgeSeverity.Downloading"/> badge.</summary>
    private const string DownloadGlyph = "";

    /// <summary>
    /// Creates an information badge using the built-in icon style for the specified severity.
    /// </summary>
    /// <param name="severity">The semantic severity represented by the badge.</param>
    /// <returns>A styled icon information badge.</returns>
    public static InfoBadge Create(NavigationInfoBadgeSeverity severity)
    {
        string styleKey = severity switch
        {
            NavigationInfoBadgeSeverity.Attention or NavigationInfoBadgeSeverity.Downloading => "AttentionIconInfoBadgeStyle",
            NavigationInfoBadgeSeverity.Informational => "InformationalIconInfoBadgeStyle",
            NavigationInfoBadgeSeverity.Success => "SuccessIconInfoBadgeStyle",
            NavigationInfoBadgeSeverity.Caution => "CautionIconInfoBadgeStyle",
            NavigationInfoBadgeSeverity.Critical => "CriticalIconInfoBadgeStyle",
            _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, null)
        };

        if (App.Current.Resources.TryGetValue(styleKey, out object styleResource)
            && styleResource is Style style)
        {
            var badge = new InfoBadge { Style = style };
            if (severity == NavigationInfoBadgeSeverity.Downloading)
            {
                // The badge template scales the glyph to the badge size and draws it with the theme's badge foreground.
                badge.IconSource = new FontIconSource { Glyph = DownloadGlyph };
            }

            return badge;
        }

        throw new InvalidOperationException($"The navigation InfoBadge style '{styleKey}' is unavailable.");
    }
}
