// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO.Compression;
using Foundry.Bootstrap.Runtime;
using Xunit;

namespace Foundry.Bootstrap.Tests.Runtime;

public sealed class RuntimeArchiveTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("FoundryRuntimeArchive-").FullName;

    [Fact]
    public async Task ExtractionHandlesNestedFilesAndEmptyDirectories()
    {
        string archivePath = CreateArchive("nested/data.txt", "payload");
        using (ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Update)) archive.CreateEntry("empty/");
        string destination = Path.Combine(root, "staging");

        await RuntimeArchive.ExtractAsync(archivePath, destination, TestContext.Current.CancellationToken);

        Assert.Equal("payload", File.ReadAllText(Path.Combine(destination, "nested", "data.txt")));
        Assert.True(Directory.Exists(Path.Combine(destination, "empty")));
    }

    [Theory]
    [InlineData("../escape.exe")]
    [InlineData("nested/../../escape.exe")]
    [InlineData("file:stream")]
    public async Task UnsafeDestinationsAreRejectedBeforeExtraction(string entry)
    {
        string archive = CreateArchive(entry, "candidate");
        string destination = Path.Combine(root, "staging");

        await Assert.ThrowsAsync<InvalidDataException>(() => RuntimeArchive.ExtractAsync(archive, destination, TestContext.Current.CancellationToken));

        Assert.False(Directory.Exists(destination));
        Assert.False(File.Exists(Path.Combine(root, "escape.exe")));
    }

    [Fact]
    public async Task CorruptedEntryWithUnchangedLengthIsRejected()
    {
        string archivePath = CreateArchive("Foundry.Connect.exe", "candidate", CompressionLevel.NoCompression);
        byte[] bytes = File.ReadAllBytes(archivePath);
        int offset = 30 + BitConverter.ToUInt16(bytes, 26) + BitConverter.ToUInt16(bytes, 28);
        bytes[offset] ^= 1;
        File.WriteAllBytes(archivePath, bytes);

        await Assert.ThrowsAsync<InvalidDataException>(() => RuntimeArchive.ExtractAsync(
            archivePath, Path.Combine(root, "staging"), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(CompressionLevel.NoCompression)]
    [InlineData(CompressionLevel.Optimal)]
    public async Task ExtractionValidatesLargeAndEmptyEntries(CompressionLevel compression)
    {
        string archivePath = Path.Combine(root, "valid.zip");
        byte[] contents = new byte[200000];
        new Random(42).NextBytes(contents);
        using (ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            using (Stream entry = archive.CreateEntry("payload.bin", compression).Open()) entry.Write(contents);
            archive.CreateEntry("empty.bin", compression);
        }
        string destination = Path.Combine(root, "staging");

        await RuntimeArchive.ExtractAsync(archivePath, destination, TestContext.Current.CancellationToken);

        Assert.Equal(contents, File.ReadAllBytes(Path.Combine(destination, "payload.bin")));
        Assert.Empty(File.ReadAllBytes(Path.Combine(destination, "empty.bin")));
    }

    [Theory]
    [InlineData(0xA0000000)]
    [InlineData(0x400)]
    public async Task LinksAreRejectedBeforeWriting(uint attributes)
    {
        string archivePath = CreateArchive("link", "target");
        using (ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Update))
            archive.Entries[0].ExternalAttributes = unchecked((int)attributes);
        string destination = Path.Combine(root, "staging");

        await Assert.ThrowsAsync<InvalidDataException>(() => RuntimeArchive.ExtractAsync(archivePath, destination, TestContext.Current.CancellationToken));

        Assert.False(Directory.Exists(destination));
    }

    private string CreateArchive(string entry, string contents, CompressionLevel compression = CompressionLevel.Optimal)
    {
        string path = Path.Combine(root, "input.zip");
        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using var writer = new StreamWriter(archive.CreateEntry(entry, compression).Open());
        writer.Write(contents);
        return path;
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
