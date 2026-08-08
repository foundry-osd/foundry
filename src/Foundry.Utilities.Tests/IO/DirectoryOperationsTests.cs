// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Utilities.IO;

namespace Foundry.Utilities.Tests.IO;

public sealed class DirectoryOperationsTests
{
    [Fact]
    public void Recreate_RemovesNestedContentAndLeavesEmptyDirectory()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string directoryPath = Path.Combine(temporaryDirectory.Path, "target");
        string nestedDirectoryPath = Path.Combine(directoryPath, "nested");
        Directory.CreateDirectory(nestedDirectoryPath);
        File.WriteAllText(Path.Combine(nestedDirectoryPath, "content.txt"), "content");

        DirectoryOperations.Recreate(directoryPath);

        Assert.True(Directory.Exists(directoryPath));
        Assert.Empty(Directory.EnumerateFileSystemEntries(directoryPath));
    }
}
