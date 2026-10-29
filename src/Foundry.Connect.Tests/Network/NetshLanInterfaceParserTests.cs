// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Network;
using Foundry.Connect.Network.Native;

namespace Foundry.Connect.Tests.Network;

public sealed class NetshLanInterfaceParserTests
{
    private const string TwoInterfaces = """
        There are 2 interfaces on the system:

            Name             : Ethernet
            Description      : Intel(R) Ethernet Connection
            GUID             : 9f1a2b3c-0000-0000-0000-000000000001
            Physical Address : 00-11-22-33-44-55
            State            : Connected. Authentication succeeded.

            Name             : Ethernet 2
            Description      : USB Ethernet
            GUID             : 9f1a2b3c-0000-0000-0000-000000000002
            Physical Address : 00-11-22-33-44-66
            State            : Authenticating
        """;

    [Fact]
    public void Parse_ReadsNameAndStatePerInterface()
    {
        IReadOnlyList<WiredInterfaceAuthentication> result = NetshLanInterfaceParser.Parse(TwoInterfaces);

        Assert.Equal(
            [new WiredInterfaceAuthentication("Ethernet", Dot1xAuthenticationStatus.Authenticated), new WiredInterfaceAuthentication("Ethernet 2", Dot1xAuthenticationStatus.Authenticating)],
            result);
    }

    [Theory]
    [InlineData("Connected. Authentication succeeded.", Dot1xAuthenticationStatus.Authenticated)]
    [InlineData("Connected. Authentication failed.", Dot1xAuthenticationStatus.Failed)]
    [InlineData("Authenticating", Dot1xAuthenticationStatus.Authenticating)]
    [InlineData("Reauthenticating", Dot1xAuthenticationStatus.Authenticating)]
    [InlineData("Connected. Network does not support authentication.", null)]
    [InlineData("Verbunden. Authentifizierung erfolgreich.", null)]
    [InlineData("", null)]
    public void ParseState_RecognizesOnlyKnownEnglishStates(string state, Dot1xAuthenticationStatus? expected)
    {
        Assert.Equal(expected, NetshLanInterfaceParser.ParseState(state));
    }

    [Fact]
    public void Parse_WhenOutputIsUnexpected_ReturnsEmpty()
    {
        Assert.Empty(NetshLanInterfaceParser.Parse("The Wired AutoConfig Service (dot3svc) is not running."));
    }

    [Theory]
    [InlineData(Dot1xAuthenticationStatus.Failed, Dot1xAuthenticationStatus.Authenticated, Dot1xAuthenticationStatus.Authenticated)]
    [InlineData(Dot1xAuthenticationStatus.Failed, Dot1xAuthenticationStatus.Authenticating, Dot1xAuthenticationStatus.Authenticating)]
    [InlineData(Dot1xAuthenticationStatus.Failed, null, Dot1xAuthenticationStatus.Failed)]
    [InlineData(null, null, null)]
    public void Aggregate_PrefersTheBestInterface(Dot1xAuthenticationStatus? first, Dot1xAuthenticationStatus? second, Dot1xAuthenticationStatus? expected)
    {
        Assert.Equal(expected, NetshLanInterfaceParser.Aggregate([new("A", first), new("B", second)]));
    }
}
