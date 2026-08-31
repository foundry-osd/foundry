// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO.Compression;
using System.Text.Json;
using Foundry.Utilities.Diagnostics;
using Foundry.Utilities.Tests.IO;

namespace Foundry.Utilities.Tests.Diagnostics;

public sealed class SupportBundleExporterTests
{
    [Fact]
    public async Task ExportAsync_SanitizedModeRedactsSensitiveLogContent()
    {
        using var tempDirectory = new TemporaryDirectory();
        string sourcePath = Path.Combine(tempDirectory.Path, "Foundry.log");
        string destinationPath = Path.Combine(tempDirectory.Path, "export");
        await File.WriteAllTextAsync(
            sourcePath,
            "ProbeUri=https://alice:hunter2@example.test/status?token=abc#fragment TenantId=11111111-1111-1111-1111-111111111111 Password=hunter2 UserPath=C:\\Users\\alice\\file.txt",
            TestContext.Current.CancellationToken);
        var exporter = new SupportBundleExporter(new FixedTimeProvider(new DateTimeOffset(2026, 8, 31, 12, 22, 33, TimeSpan.Zero)));

        SupportBundleResult result = await exporter.ExportAsync(new SupportBundleRequest
        {
            ApplicationName = "Foundry OSD",
            ApplicationVersion = "1.2.3",
            SessionId = "ABC12345",
            DestinationDirectoryPath = destinationPath,
            LogFilePaths = [sourcePath],
            Summary = new Dictionary<string, string> { ["Mode"] = "USB" }
        }, TestContext.Current.CancellationToken);

        using ZipArchive archive = ZipFile.OpenRead(result.ArchivePath);
        string log = await ReadEntryAsync(archive, "logs/Foundry.log");
        Assert.DoesNotContain("token=abc", log, StringComparison.Ordinal);
        Assert.DoesNotContain("alice:hunter2", log, StringComparison.Ordinal);
        Assert.DoesNotContain("11111111-1111-1111-1111-111111111111", log, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", log, StringComparison.Ordinal);
        Assert.DoesNotContain("alice", log, StringComparison.Ordinal);
        Assert.Contains("https://example.test/status", log, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFiles(destinationPath, "*.tmp", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task ExportAsync_RawModePreservesOriginalLogContent()
    {
        using var tempDirectory = new TemporaryDirectory();
        string sourcePath = Path.Combine(tempDirectory.Path, "Foundry.log");
        await File.WriteAllTextAsync(sourcePath, "Password=explicit-raw-value", TestContext.Current.CancellationToken);
        var exporter = new SupportBundleExporter();

        SupportBundleResult result = await exporter.ExportAsync(new SupportBundleRequest
        {
            ApplicationName = "Foundry",
            ApplicationVersion = "1.0.0",
            SessionId = "ABC12345",
            DestinationDirectoryPath = Path.Combine(tempDirectory.Path, "export"),
            LogFilePaths = [sourcePath],
            PrivacyMode = SupportBundlePrivacyMode.Raw
        }, TestContext.Current.CancellationToken);

        using ZipArchive archive = ZipFile.OpenRead(result.ArchivePath);
        string log = await ReadEntryAsync(archive, "logs/Foundry.log");
        Assert.Equal("Password=explicit-raw-value", log);
    }

    [Fact]
    public async Task ExportAsync_WhenSourceIsMissing_RecordsOmissionWithoutFailingExport()
    {
        using var tempDirectory = new TemporaryDirectory();
        string missingPath = Path.Combine(tempDirectory.Path, "missing.log");
        var exporter = new SupportBundleExporter();

        SupportBundleResult result = await exporter.ExportAsync(new SupportBundleRequest
        {
            ApplicationName = "Foundry",
            ApplicationVersion = "1.0.0",
            SessionId = "ABC12345",
            DestinationDirectoryPath = Path.Combine(tempDirectory.Path, "export"),
            LogFilePaths = [missingPath]
        }, TestContext.Current.CancellationToken);

        using ZipArchive archive = ZipFile.OpenRead(result.ArchivePath);
        string manifestJson = await ReadEntryAsync(archive, "manifest.json");
        using JsonDocument manifest = JsonDocument.Parse(manifestJson);
        Assert.Empty(manifest.RootElement.GetProperty("includedFiles").EnumerateArray());
        Assert.Single(manifest.RootElement.GetProperty("omittedFiles").EnumerateArray());
    }

    [Fact]
    public async Task ExportAsync_RawModeStillSanitizesSummaryMetadata()
    {
        using var tempDirectory = new TemporaryDirectory();
        var exporter = new SupportBundleExporter();

        SupportBundleResult result = await exporter.ExportAsync(new SupportBundleRequest
        {
            ApplicationName = "Foundry",
            ApplicationVersion = "1.0.0",
            SessionId = "ABC12345",
            DestinationDirectoryPath = Path.Combine(tempDirectory.Path, "export"),
            LogFilePaths = [],
            PrivacyMode = SupportBundlePrivacyMode.Raw,
            Summary = new Dictionary<string, string>
            {
                ["Password=key-secret"] = "TenantId=11111111-1111-1111-8111-111111111111"
            }
        }, TestContext.Current.CancellationToken);

        using ZipArchive archive = ZipFile.OpenRead(result.ArchivePath);
        string summary = await ReadEntryAsync(archive, "summary.json");
        Assert.DoesNotContain("key-secret", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("11111111-1111-1111-8111-111111111111", summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportAsync_ManifestMapsDuplicateSourceNamesToUniqueArchiveEntries()
    {
        using var tempDirectory = new TemporaryDirectory();
        string firstDirectory = Path.Combine(tempDirectory.Path, "first");
        string secondDirectory = Path.Combine(tempDirectory.Path, "second");
        Directory.CreateDirectory(firstDirectory);
        Directory.CreateDirectory(secondDirectory);
        string firstPath = Path.Combine(firstDirectory, "Foundry.log");
        string secondPath = Path.Combine(secondDirectory, "Foundry.log");
        await File.WriteAllTextAsync(firstPath, "first", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(secondPath, "second", TestContext.Current.CancellationToken);
        var exporter = new SupportBundleExporter();

        SupportBundleResult result = await exporter.ExportAsync(new SupportBundleRequest
        {
            ApplicationName = "Foundry",
            ApplicationVersion = "1.0.0",
            SessionId = "ABC12345",
            DestinationDirectoryPath = Path.Combine(tempDirectory.Path, "export"),
            LogFilePaths = [firstPath, secondPath]
        }, TestContext.Current.CancellationToken);

        using ZipArchive archive = ZipFile.OpenRead(result.ArchivePath);
        string manifestJson = await ReadEntryAsync(archive, "manifest.json");
        using JsonDocument manifest = JsonDocument.Parse(manifestJson);
        JsonElement[] includedFiles = manifest.RootElement.GetProperty("includedFiles").EnumerateArray().ToArray();
        Assert.Equal(2, includedFiles.Length);
        Assert.All(includedFiles, item => Assert.Equal("Foundry.log", item.GetProperty("sourceFileName").GetString()));
        Assert.Equal("Foundry.log", includedFiles[0].GetProperty("archiveEntryName").GetString());
        Assert.Equal("Foundry-2.log", includedFiles[1].GetProperty("archiveEntryName").GetString());
    }

    private static async Task<string> ReadEntryAsync(ZipArchive archive, string path)
    {
        ZipArchiveEntry entry = Assert.Single(archive.Entries, entry => entry.FullName == path);
        await using Stream stream = entry.Open();
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
