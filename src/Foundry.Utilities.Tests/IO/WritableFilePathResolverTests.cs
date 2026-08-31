// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Utilities.IO;

namespace Foundry.Utilities.Tests.IO;

public sealed class WritableFilePathResolverTests
{
    [Fact]
    public void Resolve_ReturnsPathUnderFirstCreatableDirectory()
    {
        using var tempDirectory = new TemporaryDirectory();
        string first = Path.Combine(tempDirectory.Path, "first");
        string second = Path.Combine(tempDirectory.Path, "second");

        string result = WritableFilePathResolver.Resolve([first, second], "foundry.log");

        Assert.Equal(Path.Combine(first, "foundry.log"), result);
        Assert.True(Directory.Exists(first));
        Assert.False(Directory.Exists(second));
    }

    [Fact]
    public void Resolve_WhenCandidateCannotBeCreated_UsesNextCandidate()
    {
        using var tempDirectory = new TemporaryDirectory();
        string filePath = Path.Combine(tempDirectory.Path, "not-a-directory");
        File.WriteAllText(filePath, "content");
        string fallback = Path.Combine(tempDirectory.Path, "fallback");

        string result = WritableFilePathResolver.Resolve([filePath, fallback], "foundry.log");

        Assert.Equal(Path.Combine(fallback, "foundry.log"), result);
    }

    [Fact]
    public void Resolve_IgnoresBlankCandidates()
    {
        using var tempDirectory = new TemporaryDirectory();

        string result = WritableFilePathResolver.Resolve(
            [string.Empty, "   ", tempDirectory.Path],
            "foundry.log");

        Assert.Equal(Path.Combine(tempDirectory.Path, "foundry.log"), result);
    }

    [Fact]
    public void Resolve_WhenAllCandidatesFail_ReturnsFileName()
    {
        using var tempDirectory = new TemporaryDirectory();
        string firstFile = Path.Combine(tempDirectory.Path, "first-file");
        string secondFile = Path.Combine(tempDirectory.Path, "second-file");
        File.WriteAllText(firstFile, "content");
        File.WriteAllText(secondFile, "content");

        string result = WritableFilePathResolver.Resolve([firstFile, secondFile], "foundry.log");

        Assert.Equal("foundry.log", result);
    }

    [Fact]
    public void Resolve_WhenTargetFileCannotBeOpened_UsesNextCandidate()
    {
        using var tempDirectory = new TemporaryDirectory();
        string first = Path.Combine(tempDirectory.Path, "first");
        string second = Path.Combine(tempDirectory.Path, "second");
        Directory.CreateDirectory(Path.Combine(first, "foundry.log"));

        string result = WritableFilePathResolver.Resolve([first, second], "foundry.log");

        Assert.Equal(Path.Combine(second, "foundry.log"), result);
    }
}
