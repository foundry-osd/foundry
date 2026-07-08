// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net;
using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.WinPe;

public sealed class PowerShellGalleryModuleSearchServiceTests
{
    private const string SampleFeed = """
    <feed xmlns="http://www.w3.org/2005/Atom"
          xmlns:d="http://schemas.microsoft.com/ado/2007/08/dataservices"
          xmlns:m="http://schemas.microsoft.com/ado/2007/08/dataservices/metadata">
      <entry>
        <title type="text">Pester</title>
        <content type="application/zip" src="https://example/package/Pester/5.5.0" />
        <m:properties>
          <d:Version>5.5.0</d:Version>
          <d:Authors>Pester Team</d:Authors>
          <d:Description>Test and mock framework.</d:Description>
        </m:properties>
      </entry>
      <entry>
        <title type="text">PSReadLine</title>
        <m:properties>
          <d:Version>2.3.4</d:Version>
          <d:Authors>Microsoft</d:Authors>
          <d:Description>Command line editing.</d:Description>
        </m:properties>
      </entry>
    </feed>
    """;

    [Fact]
    public async Task SearchAsync_ParsesModulesFromAtomFeed()
    {
        var service = new PowerShellGalleryModuleSearchService(
            new HttpClient(new StubHandler(HttpStatusCode.OK, SampleFeed)));

        WinPeResult<IReadOnlyList<PowerShellGalleryModule>> result =
            await service.SearchAsync("test", 20, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.Equal(["Pester", "PSReadLine"], result.Value!.Select(module => module.Name));
        PowerShellGalleryModule pester = result.Value![0];
        Assert.Equal("5.5.0", pester.Version);
        Assert.Equal("Pester Team", pester.Authors);
        Assert.Equal("Test and mock framework.", pester.Description);
    }

    [Fact]
    public async Task SearchAsync_WhenTermEmpty_ReturnsEmptyWithoutRequest()
    {
        var handler = new StubHandler(HttpStatusCode.InternalServerError, string.Empty);
        var service = new PowerShellGalleryModuleSearchService(new HttpClient(handler));

        WinPeResult<IReadOnlyList<PowerShellGalleryModule>> result =
            await service.SearchAsync("   ", 20, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
        Assert.False(handler.WasCalled);
    }

    private sealed class StubHandler(HttpStatusCode statusCode, string content) : HttpMessageHandler
    {
        public bool WasCalled { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            WasCalled = true;
            return Task.FromResult(new HttpResponseMessage(statusCode) { Content = new StringContent(content) });
        }
    }
}
