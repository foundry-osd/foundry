// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net;
using System.Security.Cryptography;
using System.Text;
using Foundry.Core.Services.Catalog;

namespace Foundry.Core.Tests.Catalog;

public sealed class VerifiedCatalogSnapshotAcquirerTests
{
    [Fact]
    public async Task AcquireAsync_PreservesOriginalXmlBytesAndContentAddressedRevision()
    {
        byte[] bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("<Catalog schemaVersion=\"4\"/>\r\n")).ToArray();
        using var client = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) })));
        var result = await new VerifiedCatalogSnapshotAcquirer(client).AcquireAsync(VerifiedCatalogSources.OperatingSystems, VerifiedCatalogSources.GetUri(VerifiedCatalogSources.OperatingSystems));
        Assert.Equal(bytes, result.Content);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), result.Sha256);
        Assert.Equal("sha256:" + result.Sha256.ToLowerInvariant(), result.Revision);
    }

    [Theory]
    [InlineData("http://raw.githubusercontent.com/foundry-osd/catalog/refs/heads/main/Cache/OS/OperatingSystem.xml")]
    [InlineData("https://example.test/catalog.xml")]
    [InlineData("https://user@raw.githubusercontent.com/foundry-osd/catalog/refs/heads/main/Cache/OS/OperatingSystem.xml")]
    [InlineData("https://raw.githubusercontent.com/foundry-osd/catalog/refs/heads/main/Cache/OS/OperatingSystem.xml#untrusted")]
    public async Task AcquireAsync_RejectsUnapprovedSourceBeforeHttp(string source)
    {
        using var client = new HttpClient(new Handler((_, _) => throw new Xunit.Sdk.XunitException("HTTP must not run.")));
        await Assert.ThrowsAsync<InvalidDataException>(() => new VerifiedCatalogSnapshotAcquirer(client).AcquireAsync(VerifiedCatalogSources.OperatingSystems, new Uri(source)));
    }

    [Fact]
    public async Task AcquireAsync_RejectsOversizedMetadata()
    {
        var content = new ByteArrayContent([]);
        content.Headers.ContentLength = 32L * 1024 * 1024 + 1;
        using var client = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content })));
        await Assert.ThrowsAsync<InvalidDataException>(() => new VerifiedCatalogSnapshotAcquirer(client).AcquireAsync(VerifiedCatalogSources.OperatingSystems, VerifiedCatalogSources.GetUri(VerifiedCatalogSources.OperatingSystems)));
    }

    [Fact]
    public async Task AcquireAsync_PreservesCallerCancellation()
    {
        using var client = new HttpClient(new Handler(async (_, token) => { await Task.Delay(Timeout.Infinite, token); return new(HttpStatusCode.OK); }));
        using var cancellation = new CancellationTokenSource(30);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new VerifiedCatalogSnapshotAcquirer(client).AcquireAsync(VerifiedCatalogSources.OperatingSystems, VerifiedCatalogSources.GetUri(VerifiedCatalogSources.OperatingSystems), cancellation.Token));
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
