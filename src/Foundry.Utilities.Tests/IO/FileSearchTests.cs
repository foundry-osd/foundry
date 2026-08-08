// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Utilities.IO;

namespace Foundry.Utilities.Tests.IO;

public sealed class FileSearchTests
{
    [Fact]
    public void ContainsRecursive_WhenMatchExistsInNestedDirectory_ReturnsTrue()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string nestedDirectory = Path.Combine(temporaryDirectory.Path, "nested");
        Directory.CreateDirectory(nestedDirectory);
        File.WriteAllText(Path.Combine(nestedDirectory, "driver.inf"), "content");

        bool found = FileSearch.ContainsRecursive(temporaryDirectory.Path, "*.inf");

        Assert.True(found);
    }

    [Fact]
    public void ContainsRecursive_WhenNoFileMatches_ReturnsFalse()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(temporaryDirectory.Path, "driver.txt"), "content");

        bool found = FileSearch.ContainsRecursive(temporaryDirectory.Path, "*.inf");

        Assert.False(found);
    }
}
