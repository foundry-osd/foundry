// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Foundry.Utilities.Serialization;

namespace Foundry.Utilities.Tests.Serialization;

public sealed class JsonObjectSequenceTests
{
    [Fact]
    public void Parse_WhenInputIsNull_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => JsonObjectSequence.Parse(null!));
    }

    [Fact]
    public void Parse_WhenInputIsBlank_ReturnsEmptySequence()
    {
        IReadOnlyList<JsonElement> elements = JsonObjectSequence.Parse("  ");

        Assert.Empty(elements);
    }

    [Fact]
    public void Parse_WhenRootIsObject_ReturnsIndependentObject()
    {
        IReadOnlyList<JsonElement> elements = JsonObjectSequence.Parse("""{"Name":"Disk"}""");

        JsonElement element = Assert.Single(elements);
        Assert.Equal("Disk", element.GetProperty("Name").GetString());
    }

    [Fact]
    public void Parse_WhenRootIsArray_ReturnsIndependentObjectsInOrder()
    {
        IReadOnlyList<JsonElement> elements = JsonObjectSequence.Parse(
            """[{"Number":1},{"Number":2}]""");

        Assert.Collection(
            elements,
            element => Assert.Equal(1, element.GetProperty("Number").GetInt32()),
            element => Assert.Equal(2, element.GetProperty("Number").GetInt32()));
    }

    [Fact]
    public void Parse_WhenJsonIsMalformed_ThrowsJsonException()
    {
        Assert.ThrowsAny<JsonException>(() => JsonObjectSequence.Parse("{"));
    }

    [Theory]
    [InlineData("42")]
    [InlineData("[{} , 42]")]
    public void Parse_WhenPayloadDoesNotContainOnlyObjects_ThrowsJsonException(string json)
    {
        Assert.Throws<JsonException>(() => JsonObjectSequence.Parse(json));
    }
}
