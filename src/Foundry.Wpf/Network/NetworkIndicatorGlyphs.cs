// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Wpf.Network;

/// <summary>
/// Maps a <see cref="NetworkIndicatorState"/> to its Segoe Fluent Icons glyph. Ethernet and Wi-Fi use the same
/// code points as the Foundry Connect pages.
/// </summary>
public static class NetworkIndicatorGlyphs
{
    /// <summary>Returns the glyph character to display for <paramref name="state"/>.</summary>
    public static string For(NetworkIndicatorState state) => state switch
    {
        NetworkIndicatorState.Ethernet => "\uE839",
        NetworkIndicatorState.Wifi => "\uE701",
        NetworkIndicatorState.Limited => "\uE871",
        _ => "\uF384"
    };
}
