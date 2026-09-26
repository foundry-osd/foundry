// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using Foundry.Core.Models.PreOobe;
using Foundry.Deploy.Services.Deployment.PreOobe;

namespace Foundry.Deploy.Tests;

public sealed class PreOobeRuntimeResolverTests
{
    [Fact]
    public async Task VerifiedOfflineCompanion_DoesNotUseNetworkOrWritableCache()
    {
        using var fixture = new Fixture();
        using var content = NativeRuntimeFixture.Create(fixture.Root);
        var resolver = new PreOobeRuntimeResolver(new HttpClient(new RejectingHandler()));
        string resolved = await resolver.ResolveAsync(new() { ReleaseTag = "local", Assets = [content.RuntimeAsset] },
            content.RuntimeAsset, [content.RuntimeArchivePath], Path.Combine(fixture.Root, "unused"), TestContext.Current.CancellationToken);
        Assert.Equal(content.RuntimeArchivePath, resolved);
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "unused")));
    }

    [Theory]
    [InlineData("../outside.exe")]
    [InlineData("folder\\bad.exe")]
    [InlineData("NUL")]
    [InlineData("Foundry.PostInstall.exe")]
    public void UnsafeArchiveEntry_IsRejectedBeforeExtraction(string extra)
    {
        using var fixture = new Fixture();
        string archivePath = Path.Combine(fixture.Root, "unsafe.zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            foreach (string name in new[] { "Foundry.PostInstall.exe", "Launch.cmd", extra })
            {
                using var stream = archive.CreateEntry(name).Open();
                stream.WriteByte(1);
            }
        }
        using var opened = ZipFile.OpenRead(archivePath);
        Assert.Throws<InvalidDataException>(() => PreOobeRuntimeResolver.ValidateEntries(opened,
            new PreOobeRuntimeAsset { ExpandedLength = 3, EntryCount = 3 }));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public async Task Transfer_RejectsOverlongAndTruncatedContent(long expected)
    {
        using var input = new MemoryStream([1, 2, 3]);
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(() => PreOobeRuntimeResolver.CopyBoundedAsync(input, output, expected, TestContext.Current.CancellationToken));
        Assert.True(output.Length <= expected);
    }

    [Fact]
    public async Task Download_UsesPinnedTagAndDeletesInvalidCandidate()
    {
        using var fixture = new Fixture();
        using var content = NativeRuntimeFixture.Create(fixture.Root);
        var handler = new CapturingHandler(File.ReadAllBytes(content.RuntimeArchivePath));
        var resolver = new PreOobeRuntimeResolver(new HttpClient(handler));
        string cache = Path.Combine(fixture.Root, "cache");
        var mismatched = content.RuntimeAsset with { ArchiveSha256 = new string('0', 64) };
        await Assert.ThrowsAsync<InvalidDataException>(() => resolver.ResolveAsync(new() { ReleaseTag = "v26.9.26.1" },
            mismatched, [], cache, TestContext.Current.CancellationToken));
        Assert.Equal("https://github.com/foundry-osd/foundry/releases/download/v26.9.26.1/Foundry.PostInstall-win-x64.zip", handler.Url);
        Assert.Empty(Directory.GetFiles(cache));
    }

    private sealed class RejectingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Offline resolution must not access the network.");
    }

    private sealed class CapturingHandler(byte[] bytes) : HttpMessageHandler
    {
        public string? Url { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Url = request.RequestUri!.AbsoluteUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        }
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "Foundry.Deploy.Tests", Guid.NewGuid().ToString("N"));
        public Fixture() => Directory.CreateDirectory(Root);
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
