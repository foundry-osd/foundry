// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Network;
using Foundry.Connect.Network.Policy;

namespace Foundry.Connect.Tests.Network;

public sealed class ConnectivityEvaluatorTests
{
    private static readonly NcsiEndpoint Primary = ConnectivityEvaluator.Endpoints[0];
    private static readonly NcsiEndpoint Secondary = ConnectivityEvaluator.Endpoints[1];

    public static TheoryData<string, ProbeObservation[], InternetStatus> Scenarios => new()
    {
        { "both_match", [new(Primary, true, 200, "Microsoft Connect Test"), new(Secondary, true, 200, "Microsoft NCSI")], InternetStatus.Online },
        { "one_match_one_silent", [new(Primary, true, 200, "Microsoft Connect Test"), new(Secondary, false, null, null)], InternetStatus.Online },
        { "redirect", [new(Primary, true, 302, string.Empty), new(Secondary, true, 302, string.Empty)], InternetStatus.CaptivePortal },
        { "portal_page_200", [new(Primary, true, 200, "<html>Sign in</html>"), new(Secondary, false, null, null)], InternetStatus.CaptivePortal },
        { "body_with_newline", [new(Primary, true, 200, "Microsoft Connect Test\n"), new(Secondary, false, null, null)], InternetStatus.CaptivePortal },
        { "network_auth_511", [new(Primary, true, 511, string.Empty), new(Secondary, false, null, null)], InternetStatus.CaptivePortal },
        { "proxy_407", [new(Primary, true, 407, string.Empty), new(Secondary, true, 407, string.Empty)], InternetStatus.CaptivePortal },
        { "nothing", [new(Primary, false, null, null), new(Secondary, false, null, null)], InternetStatus.NoInternet },
        { "empty", [], InternetStatus.NoInternet }
    };

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void Evaluate_ScenarioTable(string scenario, ProbeObservation[] observations, InternetStatus expected)
    {
        Assert.True(expected == ConnectivityEvaluator.Evaluate(observations), scenario);
    }

    [Fact]
    public void Endpoints_AreTheInternalNcsiConstants()
    {
        Assert.Equal(
            ["http://www.msftconnecttest.com/connecttest.txt", "http://www.msftncsi.com/ncsi.txt"],
            ConnectivityEvaluator.Endpoints.Select(static endpoint => endpoint.Uri.ToString()));
        Assert.Equal(["Microsoft Connect Test", "Microsoft NCSI"], ConnectivityEvaluator.Endpoints.Select(static endpoint => endpoint.ExpectedBody));
    }
}
