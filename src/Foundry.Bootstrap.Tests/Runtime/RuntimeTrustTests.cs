// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net;
using System.Text.Json;
using Foundry.Bootstrap.Runtime;
using Serilog;
using Xunit;

namespace Foundry.Bootstrap.Tests.Runtime;

public sealed class RuntimeTrustTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "FoundryRuntimeTrust-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task UntrustedRemovableCacheCannotSupplyAnExecutable(bool skipLookup, bool forgeCurrentManifest)
    {
        string winPeRoot = Path.Combine(root, "BootImage");
        string runtimeRoot = Path.Combine(root, "RemovableCache", "Runtime");
        string cache = Path.Combine(runtimeRoot, "Foundry.Connect", "win-x64");
        Directory.CreateDirectory(cache);
        File.WriteAllText(Path.Combine(cache, "Foundry.Connect.exe"), "substituted executable");
        File.WriteAllText(Path.Combine(cache, "Foundry.Core.dll"), "substituted dependency");
        File.WriteAllText(Path.Combine(cache, "manifest"),
            "Asset=Foundry.Connect-win-x64.zip\nArchiveSha256=" + new string('A', 64));
        using var client = new HttpClient(new ReleaseHandler(forgeCurrentManifest));
        using var logger = new LoggerConfiguration().CreateLogger();
        var resolver = new RuntimeResolver(winPeRoot, runtimeRoot, "win-x64", client, logger,
            getEnvironmentVariable: _ => null);

        await Assert.ThrowsAsync<InvalidDataException>(() => resolver.ResolveAsync(
            "Foundry.Connect", skipLookup, TestContext.Current.CancellationToken));
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    private sealed class ReleaseHandler(bool currentRelease) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!currentRelease || request.RequestUri?.Host != "api.github.com")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    tag_name = "v1.2.3",
                    assets = new[]
                    {
                        new
                        {
                            name = "Foundry.Connect-win-x64.zip",
                            digest = "sha256:" + new string('A', 64),
                            browser_download_url = "https://example.test/Foundry.Connect-win-x64.zip"
                        }
                    }
                }))
            });
        }
    }
}
