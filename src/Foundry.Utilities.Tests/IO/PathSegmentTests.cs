// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Utilities.IO;

namespace Foundry.Utilities.Tests.IO;

public sealed class PathSegmentTests
{
    [Fact]
    public void Sanitize_ReplacesInvalidFilenameCharactersAndSpaces()
    {
        string sanitized = PathSegment.Sanitize(" Folder Name<>:*? ");

        Assert.Equal("Folder_Name_____", sanitized);
    }

    [Fact]
    public void Sanitize_WhenValueIsBlank_ReturnsDefaultFallback()
    {
        string sanitized = PathSegment.Sanitize("  ");

        Assert.Equal("item", sanitized);
    }

    [Fact]
    public void Sanitize_WhenValueIsBlank_ReturnsSpecifiedFallback()
    {
        string sanitized = PathSegment.Sanitize(null, "fallback");

        Assert.Equal("fallback", sanitized);
    }

    [Fact]
    public void Sanitize_WhenValueAndFallbackAreBlank_ReturnsDefaultFallback()
    {
        string sanitized = PathSegment.Sanitize(" ", "  ");

        Assert.Equal("item", sanitized);
    }

    [Fact]
    public void Sanitize_WhenValueIsBlank_SanitizesSpecifiedFallback()
    {
        string sanitized = PathSegment.Sanitize(null, "fallback name?");

        Assert.Equal("fallback_name_", sanitized);
    }
}
