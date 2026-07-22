// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net;
using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.WinPe;

public sealed class PowerShell7ReleaseServiceTests
{
    private const string SampleJson = """
    [
      {
        "tag_name": "v7.6.0-preview.1",
        "prerelease": true,
        "draft": false,
        "assets": [ { "name": "PowerShell-7.6.0-preview.1-win-x64.zip", "browser_download_url": "https://example/preview.zip" } ]
      },
      {
        "tag_name": "v7.5.8",
        "prerelease": false,
        "draft": false,
        "assets": [
          { "name": "PowerShell-7.5.8-win-x64.zip", "browser_download_url": "https://example/7.5.8-x64.zip" },
          { "name": "PowerShell-7.5.8-win-arm64.zip", "browser_download_url": "https://example/7.5.8-arm64.zip" }
        ]
      },
      {
        "tag_name": "v7.4.17",
        "prerelease": false,
        "draft": false,
        "assets": [ { "name": "PowerShell-7.4.17-win-x64.zip", "browser_download_url": "https://example/7.4.17-x64.zip" } ]
      },
      {
        "tag_name": "v7.3.99",
        "prerelease": false,
        "draft": false,
        "assets": [ { "name": "PowerShell-7.3.99-win-x64.zip", "browser_download_url": "https://example/7.3.99-x64.zip" } ]
      }
    ]
    """;

    [Fact]
    public async Task GetLatestStableReleasesAsync_ForX64_SkipsPrereleaseAndLimitsCount()
    {
        var service = new PowerShell7ReleaseService(CreateClient(SampleJson));

        WinPeResult<IReadOnlyList<PowerShell7Release>> result =
            await service.GetLatestStableReleasesAsync(WinPeArchitecture.X64, count: 2, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.Equal(["7.5.8", "7.4.17"], result.Value!.Select(release => release.Version));
        Assert.Equal("https://example/7.5.8-x64.zip", result.Value![0].DownloadUrl);
        Assert.Equal("PowerShell-7.5.8-win-x64.zip", result.Value![0].AssetName);
    }

    [Fact]
    public async Task GetLatestStableReleasesAsync_ForArm64_OnlyReturnsReleasesWithArm64Asset()
    {
        var service = new PowerShell7ReleaseService(CreateClient(SampleJson));

        WinPeResult<IReadOnlyList<PowerShell7Release>> result =
            await service.GetLatestStableReleasesAsync(WinPeArchitecture.Arm64, count: 3, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.Equal(["7.5.8"], result.Value!.Select(release => release.Version));
        Assert.EndsWith("-win-arm64.zip", result.Value![0].AssetName);
    }

    [Fact]
    public async Task GetLatestStableReleasesAsync_WhenRequestFails_ReturnsFailure()
    {
        var service = new PowerShell7ReleaseService(new HttpClient(new StubHandler(HttpStatusCode.ServiceUnavailable, string.Empty)));

        WinPeResult<IReadOnlyList<PowerShell7Release>> result =
            await service.GetLatestStableReleasesAsync(WinPeArchitecture.X64, count: 3, CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    private static HttpClient CreateClient(string json)
    {
        return new HttpClient(new StubHandler(HttpStatusCode.OK, json));
    }

    private sealed class StubHandler(HttpStatusCode statusCode, string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(json)
            });
        }
    }
}
