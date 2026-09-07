// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO.Compression;
using System.Text.Json;
using Foundry.Utilities.Diagnostics;
using Foundry.Utilities.Tests.IO;
namespace Foundry.Utilities.Tests.Diagnostics;

public sealed class SupportBundleBoundaryTests
{
    [Theory]
    [InlineData("ExpectedGroupTag=secret-value")]
    [InlineData("\"ExpectedGroupTag\": \"secret-value\"")]
    [InlineData("\"Password\": \"prefix\\\"secret-value\"")]
    [InlineData("Password=\"first\r\nsecret-value\"")]
    public void Sanitize_ProducerFieldsAndEscapedQuotedValues(string value)
    {
        Assert.DoesNotContain("secret-value", DiagnosticContentSanitizer.SanitizeMultiline(value));
    }

    [Theory]
    [InlineData("../secret.log")]
    [InlineData("C:\\secret.log")]
    [InlineData("secret.xml")]
    [InlineData("secret.pfx")]
    public async Task Export_RejectsUnsafeOrUnsupportedNames(string name)
    {
        using var temp = new TemporaryDirectory();
        string file = Path.Combine(temp.Path, "input.log");
        await File.WriteAllTextAsync(file, "secret-value", TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<IOException>(() => ExportAsync(Request(temp.Path, new SupportBundleSource(file, name, SupportBundleSourceFormat.NativeText, true))));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Export_MalformedJsonRequiredFailsOptionalOmits(bool required)
    {
        using var temp = new TemporaryDirectory();
        string file = Path.Combine(temp.Path, "secret-value.json");
        await File.WriteAllTextAsync(file, "{bad secret-value", TestContext.Current.CancellationToken);
        var request = Request(temp.Path, new SupportBundleSource(file, "actions.json", SupportBundleSourceFormat.ActionResultJson, required));
        if (required)
        {
            var error = await Assert.ThrowsAsync<IOException>(() => ExportAsync(request));
            Assert.DoesNotContain("secret-value", error.ToString());
            Assert.Empty(Directory.GetFiles(temp.Path, "*.zip"));
        }
        else
        {
            var result = await ExportAsync(request);
            using var archive = ZipFile.OpenRead(result.ArchivePath);
            Assert.Null(archive.GetEntry("results/actions.json"));
            using var reader = new StreamReader(archive.GetEntry("manifest.json")!.Open());
            Assert.DoesNotContain("secret-value", await reader.ReadToEndAsync(TestContext.Current.CancellationToken));
            Assert.Single(result.OmittedFiles);
        }
    }

    [Fact]
    public async Task Export_ActionResultsAllowlistDropsUnknownFields()
    {
        using var temp = new TemporaryDirectory();
        string file = Path.Combine(temp.Path, "actions.json");
        await File.WriteAllTextAsync(file, "[{\"id\":\"driver-pack\",\"status\":\"failed\",\"exitCode\":1618,\"rebootRequired\":true,\"attempt\":1,\"errorCode\":\"native_exit_failed\",\"output\":\"secret-value\"}]", TestContext.Current.CancellationToken);
        var result = await ExportAsync(Request(temp.Path, new SupportBundleSource(file, "actions.json", SupportBundleSourceFormat.ActionResultJson, true)));
        using var archive = ZipFile.OpenRead(result.ArchivePath);
        Assert.NotNull(archive.GetEntry("results/actions.json"));
        using var reader = new StreamReader(archive.GetEntry("results/actions.json")!.Open());
        string json = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("secret-value", json);
        using var parsed = JsonDocument.Parse(json);
        Assert.Equal(1618, parsed.RootElement[0].GetProperty("exitCode").GetInt32());
    }

    [Theory]
    [InlineData("id", "secret-value")]
    [InlineData("status", "secret-value")]
    [InlineData("errorCode", "secret-value")]
    public async Task Export_ActionResultsRejectsArbitraryAllowedStringValues(string field, string value)
    {
        using var temp = new TemporaryDirectory();
        string file = Path.Combine(temp.Path, "actions.json");
        var record = new Dictionary<string, object> { ["id"] = "driver-pack", ["status"] = "failed", [field] = value };
        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(new[] { record }), TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<IOException>(() => ExportAsync(Request(temp.Path, new SupportBundleSource(file, "actions.json", SupportBundleSourceFormat.ActionResultJson, true))));
    }

    [Fact]
    public async Task Export_RejectsOversizedSourcePreservingExistingArchive()
    {
        using var temp = new TemporaryDirectory();
        string file = Path.Combine(temp.Path, "large.log");
        await using (var stream = File.Create(file)) { stream.SetLength(10 * 1024 * 1024 + 1); }
        string previous = Path.Combine(temp.Path, "previous.zip");
        await File.WriteAllTextAsync(previous, "previous", TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<IOException>(() => ExportAsync(Request(temp.Path, new SupportBundleSource(file, "large.log", SupportBundleSourceFormat.ApplicationText, true))));
        Assert.Equal("previous", await File.ReadAllTextAsync(previous, TestContext.Current.CancellationToken));
        Assert.Single(Directory.GetFiles(temp.Path, "*.zip"));
        Assert.Empty(Directory.GetFiles(temp.Path, "*.tmp"));
    }

    [Fact]
    public void Sanitize_HistoricalAutopilotTimeoutProducer()
    {
        string value = DiagnosticContentSanitizer.Sanitize("Windows Autopilot group tag update timed out. SerialNumber=SER123, ImportId=8a8098ce-cf30-4db3-a602-f09c770abca1, ExpectedGroupTag=Finance-Europe.");
        Assert.DoesNotContain("Finance-Europe", value);
        Assert.DoesNotContain("SER123", value);
    }

    [Theory]
    [InlineData(33, 1)]
    [InlineData(5, 10 * 1024 * 1024)]
    public async Task Export_EnforcesSourceCountAndTotalInputLimits(int count, int size)
    {
        using var temp = new TemporaryDirectory();
        string file = Path.Combine(temp.Path, "input.log");
        await using (var stream = File.Create(file)) { stream.SetLength(size); }
        var sources = Enumerable.Range(0, count).Select(i => new SupportBundleSource(file, $"source-{i}.log", SupportBundleSourceFormat.ApplicationText, true)).ToArray();
        await Assert.ThrowsAsync<IOException>(() => ExportAsync(Request(temp.Path, sources)));
        Assert.Empty(Directory.GetFiles(temp.Path, "*.zip"));
    }

    [Fact]
    public async Task Export_RejectsDuplicateNames()
    {
        using var temp = new TemporaryDirectory();
        string file = Path.Combine(temp.Path, "input.log");
        await File.WriteAllTextAsync(file, "ordinary", TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<IOException>(() => ExportAsync(Request(temp.Path,
            new SupportBundleSource(file, "same.log", SupportBundleSourceFormat.ApplicationText, true),
            new SupportBundleSource(file, "SAME.log", SupportBundleSourceFormat.ApplicationText, true))));
    }

    [Fact]
    public async Task Export_CancellationDoesNotPublish()
    {
        using var temp = new TemporaryDirectory();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ExportAsync(Request(temp.Path), cancellation.Token));
        Assert.Empty(Directory.GetFiles(temp.Path, "*.zip"));
    }

    [Theory]
    [InlineData("[{\"id\":\"driver-pack\",\"status\":\"failed\",\"attempt\":4}]")]
    [InlineData("[{\"id\":\"driver-pack\",\"status\":\"failed\",\"rebootRequired\":\"secret-value\"}]")]
    [InlineData("[{\"id\":\"driver-pack\",\"status\":\"failed\",\"startedUtc\":\"secret-value\"}]")]
    [InlineData("[{\"id\":\"driver-pack\",\"status\":\"failed\",\"status\":\"succeeded\"}]")]
    [InlineData("{\"Password\":\"secret-value\"}")]
    public async Task Export_RejectsMalformedActionFieldTypesOrGenericJson(string json)
    {
        using var temp = new TemporaryDirectory();
        string file = Path.Combine(temp.Path, "actions.json");
        await File.WriteAllTextAsync(file, json, TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<IOException>(() => ExportAsync(Request(temp.Path, new SupportBundleSource(file, "actions.json", SupportBundleSourceFormat.ActionResultJson, true))));
    }

    [Fact]
    public void OwnedSource_RejectsEscapesButAllowsMissingOptionalFile()
    {
        using var temp = new TemporaryDirectory();
        Assert.Throws<ArgumentException>(() => SupportBundleSourcePolicy.CreateOwned(temp.Path, "../secret.log", "native.log", SupportBundleSourceFormat.NativeText));
        var source = SupportBundleSourcePolicy.CreateOwned(temp.Path, "logs/missing.log", "native.log", SupportBundleSourceFormat.NativeText);
        Assert.False(source.Required);
        Assert.Equal(Path.Combine(temp.Path, "logs", "missing.log"), source.Path);
    }

    [Fact]
    public async Task Snapshot_StopsAtInitialLengthAndRejectsGrowth()
    {
        await using var stream = new GrowingStream();
        await Assert.ThrowsAsync<IOException>(() => SupportBundleExporter.ReadBoundedSnapshotAsync(stream, 100, TestContext.Current.CancellationToken));
        Assert.Equal(4, stream.BytesRead);
    }

    private sealed class GrowingStream : MemoryStream
    {
        private bool _read;
        public int BytesRead { get; private set; }
        public GrowingStream() : base(new byte[8]) { }
        public override long Length => _read ? 8 : 4;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            _read = true;
            BytesRead += buffer.Length;
            return base.ReadAsync(buffer, cancellationToken);
        }
    }

    [Fact]
    public void OwnedSource_RejectsExistingReparseAncestorWithoutHostMutation()
    {
        using var temp = new TemporaryDirectory();
        string parent = Path.Combine(temp.Path, "linked");
        string path = Path.Combine(parent, "native.log");
        Assert.Throws<IOException>(() => SupportBundleSourcePolicy.RejectReparseChain(path,
            candidate => candidate == parent ? FileAttributes.Directory | FileAttributes.ReparsePoint : FileAttributes.Normal));
    }

    [Fact]
    public async Task Export_ManyOptionalInputsProduceBoundedOmissionMetadata()
    {
        using var temp = new TemporaryDirectory();
        var sources = Enumerable.Range(0, 1000).Select(i => new SupportBundleSource(Path.Combine(temp.Path, "missing.log"), $"source-{i}.log", SupportBundleSourceFormat.ApplicationText, false)).ToArray();
        var result = await ExportAsync(Request(temp.Path, sources));
        Assert.Equal(33, result.OmittedFiles.Count);
    }

    [Fact]
    public async Task Export_NativeDismTemplatePreservesDiagnosticCodes()
    {
        using var temp = new TemporaryDirectory();
        string file = Path.Combine(temp.Path, "dism.log");
        const string content = "2026-09-07T12:00:00+00:00 DISM exit=1; outputTruncated=False\r\nImage Version: 10.0.26100\r\nError: 87";
        await File.WriteAllTextAsync(file, content, TestContext.Current.CancellationToken);
        var result = await ExportAsync(Request(temp.Path, new SupportBundleSource(file, "dism.log", SupportBundleSourceFormat.NativeText, true)));
        using var archive = ZipFile.OpenRead(result.ArchivePath);
        using var reader = new StreamReader(archive.GetEntry("logs/dism.log")!.Open());
        Assert.Equal(content, await reader.ReadToEndAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RollingSources_SelectsOnlyCanonicalAndNumericRolls()
    {
        using var temp = new TemporaryDirectory();
        foreach (string name in new[] { "Foundry.log", "Foundry_001.log", "Foundry.2.log", "Foundry-secret.log", "Foundry_abc.log", "Other.log" })
        { await File.WriteAllTextAsync(Path.Combine(temp.Path, name), "ordinary", TestContext.Current.CancellationToken); }
        var sources = SupportBundleSourcePolicy.CreateRollingLogs(temp.Path, "Foundry.log", "app");
        Assert.Equal(3, sources.Count);
        Assert.True(sources[0].Required);
        Assert.All(sources.Skip(1), source => Assert.False(source.Required));
        Assert.DoesNotContain(sources, source => source.Path.Contains("secret", StringComparison.Ordinal));
        Assert.Equal(3, sources.Select(source => source.ArchiveName).Distinct().Count());
    }

    [Fact]
    public async Task RollingSources_BoundsRecentSelectionAndIgnoresNestedFiles()
    {
        using var temp = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Path, "nested"));
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "nested", "Foundry_999.log"), "ordinary", TestContext.Current.CancellationToken);
        for (int index = 1; index <= 8; index++)
        {
            string path = Path.Combine(temp.Path, $"Foundry_{index:D3}.log");
            await File.WriteAllTextAsync(path, "ordinary", TestContext.Current.CancellationToken);
            File.SetLastWriteTimeUtc(path, new DateTime(2026, 1, index, 0, 0, 0, DateTimeKind.Utc));
        }
        var sources = SupportBundleSourcePolicy.CreateRollingLogs(temp.Path, "Foundry.log", "app");
        Assert.Equal(5, sources.Count);
        Assert.False(sources[0].Required);
        Assert.Equal("Foundry_008.log", Path.GetFileName(sources[1].Path));
        Assert.DoesNotContain(sources, source => source.Path.Contains("nested", StringComparison.Ordinal));
    }

    private static Task<SupportBundleResult> ExportAsync(SupportBundleRequest request, CancellationToken? token = null)
        => new SupportBundleExporter().ExportAsync(request, token ?? TestContext.Current.CancellationToken);

    private static SupportBundleRequest Request(string root, params SupportBundleSource[] sources) => new()
    {
        ApplicationName = "Foundry",
        ApplicationVersion = "1.0",
        SessionId = "session",
        DestinationDirectoryPath = root,
        Sources = sources
    };
}
