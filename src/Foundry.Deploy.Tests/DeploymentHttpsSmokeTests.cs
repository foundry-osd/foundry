// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Services.Catalog;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

/// <summary>
/// Opt-in network checks of platform trust, hostname and validity using the real Deploy clients.
/// These require direct access to the public test endpoints and do not install certificates.
/// </summary>
public sealed class DeploymentHttpsSmokeTests
{
    [Theory(Explicit = true)]
    [MemberData(nameof(DeploymentHttpClientTests.Clients), MemberType = typeof(DeploymentHttpClientTests))]
    public async Task DefaultClient_ValidatesPublicHttpsCertificates(Type serviceType, int timeoutMinutes)
    {
        HttpClient client = DeploymentHttpClientTests.GetDefaultClient(serviceType);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        lifetime.CancelAfter(TimeSpan.FromSeconds(60));
        Assert.Equal(TimeSpan.FromMinutes(timeoutMinutes), client.Timeout);

        using HttpResponseMessage valid = await client.GetAsync("https://sha256.badssl.com/", lifetime.Token);
        Assert.True(valid.IsSuccessStatusCode);
        foreach (string endpoint in new[] { "expired", "wrong.host", "self-signed" })
        {
            HttpRequestException exception = await Assert.ThrowsAsync<HttpRequestException>(
                () => client.GetStringAsync($"https://{endpoint}.badssl.com/", lifetime.Token));
            Assert.Equal(HttpRequestError.SecureConnectionError, exception.HttpRequestError);
        }
    }

    [Fact(Explicit = true)]
    public async Task CatalogServices_LoadCurrentCatalogsWithPlatformTrust()
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        lifetime.CancelAfter(TimeSpan.FromSeconds(60));
        var operatingSystems = new OperatingSystemCatalogService(NullLogger<OperatingSystemCatalogService>.Instance);
        var drivers = new DriverPackCatalogService(NullLogger<DriverPackCatalogService>.Instance);

        Assert.NotEmpty(await operatingSystems.GetCatalogAsync(lifetime.Token));
        Assert.NotEmpty(await drivers.GetCatalogAsync(lifetime.Token));
    }
}
