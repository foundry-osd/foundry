// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net;
using System.Net.Http;
using System.Text;
using Foundry.Core.Services.Runtime;

namespace Foundry.Core.Tests.Runtime;

public sealed class LatestFoundryReleaseTests
{
    [Fact]
    public async Task ReadVersionAsync_ReturnsTheLatestTagWithoutItsPrefix()
    {
        HttpRequestMessage? sent = null;
        using var client = new HttpClient(new StubHandler((request, _) =>
        {
            sent = request;
            return Task.FromResult(Json("""{ "tag_name": "v26.11.2.1", "assets": [] }"""));
        }));

        string? version = await LatestFoundryRelease.ReadVersionAsync(client, null, CancellationToken.None);

        Assert.Equal("26.11.2.1", version);
        Assert.Equal("https://api.github.com/repos/foundry-osd/foundry/releases/latest", sent!.RequestUri!.AbsoluteUri);
        Assert.Contains("application/vnd.github+json", sent.Headers.Accept.ToString(), StringComparison.Ordinal);
        Assert.NotEmpty(sent.Headers.UserAgent);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task ReadVersionAsync_ReturnsNullForAnHttpFailure(HttpStatusCode statusCode)
    {
        using var client = new HttpClient(new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(statusCode))));

        Assert.Null(await LatestFoundryRelease.ReadVersionAsync(client, null, CancellationToken.None));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("""["v26.11.2.1"]""")]
    [InlineData("""{ "tag_name": 26 }""")]
    [InlineData("""{ "tag_name": "v26.11.2" }""")]
    [InlineData("""{ "tag_name": "v1.0.0.0" }""")]
    [InlineData("""{ "tag_name": "nightly" }""")]
    public async Task ReadVersionAsync_ReturnsNullForAMalformedRelease(string body)
    {
        using var client = new HttpClient(new StubHandler((_, _) => Task.FromResult(Json(body))));

        Assert.Null(await LatestFoundryRelease.ReadVersionAsync(client, null, CancellationToken.None));
    }

    [Fact]
    public async Task ReadVersionAsync_ReturnsNullWhenTheLookupTimesOut()
    {
        using var client = new HttpClient(new StubHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Json("""{ "tag_name": "v26.11.2.1" }""");
        }));

        Assert.Null(await LatestFoundryRelease.ReadVersionAsync(client, null, TimeSpan.FromMilliseconds(50), CancellationToken.None));
    }

    [Fact]
    public async Task ReadVersionAsync_LetsCallerCancellationEscape()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        using var client = new HttpClient(new StubHandler((_, token) =>
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(Json("""{ "tag_name": "v26.11.2.1" }"""));
        }));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => LatestFoundryRelease.ReadVersionAsync(client, null, cancellation.Token));
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request, cancellationToken);
    }
}
