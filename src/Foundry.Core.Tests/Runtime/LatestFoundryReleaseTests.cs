// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net;
using System.Net.Http;
using Foundry.Core.Services.Runtime;

namespace Foundry.Core.Tests.Runtime;

public sealed class LatestFoundryReleaseTests
{
    private const string TagUrl = "https://github.com/foundry-osd/foundry/releases/tag/v26.11.2.1";

    [Fact]
    public async Task ReadVersionAsync_ReadsTheVersionFromTheRedirectLocation()
    {
        HttpRequestMessage? sent = null;
        using var client = new HttpClient(new StubHandler((request, _) =>
        {
            sent = request;
            return Task.FromResult(Redirect(TagUrl));
        }));

        string? version = await LatestFoundryRelease.ReadVersionAsync(client, null, CancellationToken.None);

        Assert.Equal("26.11.2.1", version);
        Assert.Equal(HttpMethod.Head, sent!.Method);
        Assert.Equal(LatestFoundryRelease.LatestReleasePageUrl, sent.RequestUri!.AbsoluteUri);
        Assert.NotEmpty(sent.Headers.UserAgent);
    }

    [Fact]
    public async Task ReadVersionAsync_ReadsTheVersionFromAFollowedRedirect()
    {
        // A redirect-following handler reports the final address on the response's request message.
        using var client = new HttpClient(new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Head, TagUrl)
        })));

        Assert.Equal("26.11.2.1", await LatestFoundryRelease.ReadVersionAsync(client, null, CancellationToken.None));
    }

    [Fact]
    public async Task ReadVersionAsync_ResolvesARelativeLocation()
    {
        using var client = new HttpClient(new StubHandler((_, _) =>
            Task.FromResult(Redirect("/foundry-osd/foundry/releases/tag/v26.11.2.1"))));

        Assert.Equal("26.11.2.1", await LatestFoundryRelease.ReadVersionAsync(client, null, CancellationToken.None));
    }

    [Theory]
    [InlineData("https://github.com/foundry-osd/foundry/releases")]
    [InlineData("https://github.com/foundry-osd/foundry/releases/tag/v26.11.2")]
    [InlineData("https://github.com/foundry-osd/foundry/releases/tag/v1.0.0.0")]
    [InlineData("https://github.com/foundry-osd/foundry/releases/tag/nightly")]
    [InlineData("https://github.com/foundry-osd/foundry/releases/tag/v26.11.2.1/extra")]
    [InlineData("https://github.com/other/repo/releases/tag/v26.11.2.1")]
    [InlineData("https://example.com/foundry-osd/foundry/releases/tag/v26.11.2.1")]
    [InlineData("http://github.com/foundry-osd/foundry/releases/tag/v26.11.2.1")]
    public async Task ReadVersionAsync_ReturnsNullForAnUnexpectedTarget(string location)
    {
        using var client = new HttpClient(new StubHandler((_, _) => Task.FromResult(Redirect(location))));

        Assert.Null(await LatestFoundryRelease.ReadVersionAsync(client, null, CancellationToken.None));
    }

    [Fact]
    public async Task ReadVersionAsync_ReturnsNullWhenThePageIsNotRedirected()
    {
        // 200 on the original URL: a rate-limit or error page, with no release tag to read.
        using var client = new HttpClient(new StubHandler((request, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request
        })));

        Assert.Null(await LatestFoundryRelease.ReadVersionAsync(client, null, CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Found)]
    public async Task ReadVersionAsync_ReturnsNullForAnHttpFailure(HttpStatusCode statusCode)
    {
        // Found without a Location header is a redirect that cannot be followed.
        using var client = new HttpClient(new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(statusCode))));

        Assert.Null(await LatestFoundryRelease.ReadVersionAsync(client, null, CancellationToken.None));
    }

    [Fact]
    public async Task ReadVersionAsync_ReturnsNullWhenTheLookupTimesOut()
    {
        using var client = new HttpClient(new StubHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Redirect(TagUrl);
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
            return Task.FromResult(Redirect(TagUrl));
        }));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => LatestFoundryRelease.ReadVersionAsync(client, null, cancellation.Token));
    }

    private static HttpResponseMessage Redirect(string location)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        return response;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request, cancellationToken);
    }
}
