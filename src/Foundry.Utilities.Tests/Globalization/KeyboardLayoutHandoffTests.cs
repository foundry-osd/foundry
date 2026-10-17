// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Utilities.Globalization;

namespace Foundry.Utilities.Tests.Globalization;

public sealed class KeyboardLayoutHandoffTests
{
    [Fact]
    public void TryRead_ReturnsWrittenId()
    {
        string directory = CreateDirectory();
        string path = Path.Combine(directory, "Runtime", "keyboard-layout.txt");

        try
        {
            KeyboardLayoutHandoff.Write(path, "0000080c");

            Assert.True(KeyboardLayoutHandoff.TryRead(path, out string layoutId));
            Assert.Equal("0000080C", layoutId);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-klid")]
    public void TryRead_ReturnsFalseForMissingEmptyOrInvalidFile(string? content)
    {
        string directory = CreateDirectory();
        string path = Path.Combine(directory, "keyboard-layout.txt");
        if (content is not null)
        {
            File.WriteAllText(path, content);
        }

        try
        {
            Assert.False(KeyboardLayoutHandoff.TryRead(path, out string layoutId));
            Assert.Equal(string.Empty, layoutId);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"foundry-keyboard-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
