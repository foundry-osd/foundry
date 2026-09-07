// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Tests.Configuration;

public sealed class ConnectInternetProbeSettingsTests
{
    [Fact]
    public void Defaults_EmitOneExactMicrosoftExpectationWithoutLegacyFallback()
    {
        var settings = new ConnectInternetProbeSettings();
        ConnectInternetProbeEndpoint endpoint = Assert.Single(settings.Probes);
        Assert.Equal("http://www.msftconnecttest.com/connecttest.txt", endpoint.Uri);
        Assert.Equal(200, endpoint.ExpectedStatusCode);
        Assert.Equal("Microsoft Connect Test", endpoint.ExpectedBody);
        string json = JsonSerializer.Serialize(settings);
        Assert.DoesNotContain("ProbeUris", json);
        Assert.DoesNotContain("google", json);
        Assert.Equal(endpoint, Assert.Single(JsonSerializer.Deserialize<ConnectInternetProbeSettings>(json)!.Probes));
    }
}
