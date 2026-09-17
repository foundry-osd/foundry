// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net;
using Foundry.Deploy.Services.DriverPacks;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class MicrosoftUpdateCatalogClientTests
{
    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public async Task IsAvailableAsync_WhenCallerCancels_PropagatesWithoutFallback(bool cancelOnGet, int expectedRequests)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var handler = new AvailabilityHandler(request =>
        {
            if (cancelOnGet && request.Method == HttpMethod.Head)
            {
                return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);
            }

            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        });
        using var client = new HttpClient(handler);
        var service = new MicrosoftUpdateCatalogClient(NullLogger<MicrosoftUpdateCatalogClient>.Instance, client);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.IsAvailableAsync(cancellation.Token));

        Assert.Equal(expectedRequests, handler.Requests);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public async Task IsAvailableAsync_WhenTlsFails_PropagatesWithoutFallbackOrRetry(bool failOnGet, int expectedRequests)
    {
        var failure = new HttpRequestException(HttpRequestError.SecureConnectionError, "TLS failure");
        using var handler = new AvailabilityHandler(request => failOnGet && request.Method == HttpMethod.Head
            ? new HttpResponseMessage(HttpStatusCode.MethodNotAllowed)
            : throw failure);
        using var client = new HttpClient(handler);
        var service = new MicrosoftUpdateCatalogClient(NullLogger<MicrosoftUpdateCatalogClient>.Instance, client);

        HttpRequestException actual = await Assert.ThrowsAsync<HttpRequestException>(() => service.IsAvailableAsync(TestContext.Current.CancellationToken));

        Assert.Same(failure, actual);
        Assert.Equal(expectedRequests, handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, true)]
    [InlineData(HttpStatusCode.NotFound, false)]
    public async Task IsAvailableAsync_WhenHeadIsUnsupported_PreservesGetFallback(HttpStatusCode getStatus, bool expected)
    {
        using var handler = new AvailabilityHandler(request => new HttpResponseMessage(
            request.Method == HttpMethod.Head ? HttpStatusCode.MethodNotAllowed : getStatus));
        using var client = new HttpClient(handler);
        var service = new MicrosoftUpdateCatalogClient(NullLogger<MicrosoftUpdateCatalogClient>.Instance, client);

        Assert.Equal(expected, await service.IsAvailableAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, handler.Requests);
    }

    private sealed class AvailabilityHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(response(request));
        }
    }

    [Fact]
    public void ParseDownloads_DecodesBase64HashesAndFileName()
    {
        const string html = """
                            <script type="text/javascript">
                            downloadInformation[0].files[0] = new Object();
                            downloadInformation[0].files[0].url = 'https://catalog.s.download.windowsupdate.com/c/msdownload/update/software/updt/2026/03/sample.cab';
                            downloadInformation[0].files[0].digest = 'iH8eDpa5pkZtyW2IzGWCDGhJ0e0=';
                            downloadInformation[0].files[0].sha256 = 'yxqvwrIfOgfWwAIVL2K6czq0FTGN7ZTA/iSmlyNZbO8=';
                            downloadInformation[0].files[0].fileName = 'sample.cab';
                            downloadInformation[0].files[0].architectures = 'AMD64';
                            downloadInformation[0].files[0].languages = 'en';
                            </script>
                            """;

        IReadOnlyList<MicrosoftUpdateCatalogDownload> downloads = MicrosoftUpdateCatalogClient.ParseDownloads(html, NullLogger<MicrosoftUpdateCatalogClient>.Instance);

        MicrosoftUpdateCatalogDownload download = Assert.Single(downloads);
        Assert.Equal("https://catalog.s.download.windowsupdate.com/c/msdownload/update/software/updt/2026/03/sample.cab", download.DownloadUrl);
        Assert.Equal("sample.cab", download.FileName);
        Assert.Equal("887F1E0E96B9A6466DC96D88CC65820C6849D1ED", download.Sha1);
        Assert.Equal("CB1AAFC2B21F3A07D6C002152F62BA733AB415318DED94C0FE24A69723596CEF", download.Sha256);
        Assert.Equal("AMD64", download.Architectures);
        Assert.Equal("en", download.Languages);
    }

    [Fact]
    public void ParseDownloads_WhenSha256IsEmpty_KeepsSha1()
    {
        const string html = """
                            downloadInformation[0].files[0].url = 'https://example.test/driver.cab';
                            downloadInformation[0].files[0].digest = 'iH8eDpa5pkZtyW2IzGWCDGhJ0e0=';
                            downloadInformation[0].files[0].sha256 = '';
                            downloadInformation[0].files[0].fileName = 'driver.cab';
                            """;

        MicrosoftUpdateCatalogDownload download = Assert.Single(
            MicrosoftUpdateCatalogClient.ParseDownloads(html, NullLogger<MicrosoftUpdateCatalogClient>.Instance));

        Assert.Equal("887F1E0E96B9A6466DC96D88CC65820C6849D1ED", download.Sha1);
        Assert.Equal(string.Empty, download.Sha256);
    }

    [Fact]
    public void ParseDownloads_ParsesMultipleFiles()
    {
        const string html = """
                            downloadInformation[0].files[0].url = 'https://example.test/driver-x64.cab';
                            downloadInformation[0].files[0].fileName = 'driver-x64.cab';
                            downloadInformation[0].files[1].url = 'https://example.test/driver-arm64.cab';
                            downloadInformation[0].files[1].fileName = 'driver-arm64.cab';
                            """;

        IReadOnlyList<MicrosoftUpdateCatalogDownload> downloads = MicrosoftUpdateCatalogClient.ParseDownloads(html, NullLogger<MicrosoftUpdateCatalogClient>.Instance);

        Assert.Equal(["driver-x64.cab", "driver-arm64.cab"], downloads.Select(download => download.FileName));
    }
}
