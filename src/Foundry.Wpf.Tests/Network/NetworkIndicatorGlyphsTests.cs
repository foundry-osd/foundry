// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Wpf.Network;

namespace Foundry.Wpf.Tests.Network;

public sealed class NetworkIndicatorGlyphsTests
{
    [Theory]
    [InlineData(NetworkIndicatorState.Ethernet, "\uE839")]
    [InlineData(NetworkIndicatorState.Wifi, "\uE701")]
    [InlineData(NetworkIndicatorState.Limited, "\uE871")]
    [InlineData(NetworkIndicatorState.Disconnected, "\uF384")]
    public void For_ReturnsTheGlyphOfTheState(NetworkIndicatorState state, string expected)
    {
        Assert.Equal(expected, NetworkIndicatorGlyphs.For(state));
    }

    [Fact]
    public void For_ReturnsADistinctGlyphPerState()
    {
        string[] glyphs = Enum.GetValues<NetworkIndicatorState>().Select(NetworkIndicatorGlyphs.For).ToArray();

        Assert.Equal(glyphs.Length, glyphs.Distinct().Count());
    }
}
