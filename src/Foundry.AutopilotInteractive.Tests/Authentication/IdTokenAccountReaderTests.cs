// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text;
using Foundry.AutopilotInteractive.Services.Authentication;

namespace Foundry.AutopilotInteractive.Tests.Authentication;

public sealed class IdTokenAccountReaderTests
{
    [Fact]
    public void Read_PrefersPreferredUsername()
    {
        string token = CreateToken("""{"preferred_username":"admin@contoso.com","upn":"other@contoso.com","name":"Admin"}""");

        Assert.Equal("admin@contoso.com", IdTokenAccountReader.Read(token));
    }

    [Fact]
    public void Read_FallsBackToUpnThenName()
    {
        Assert.Equal("upn@contoso.com", IdTokenAccountReader.Read(CreateToken("""{"upn":"upn@contoso.com","name":"Admin"}""")));
        Assert.Equal("Admin", IdTokenAccountReader.Read(CreateToken("""{"preferred_username":" ","name":"Admin"}""")));
    }

    [Theory]
    [InlineData("not-a-token")]
    [InlineData("a.%%%.c")]
    [InlineData("a.e30.c")]
    public void Read_ReturnsNull_ForMalformedToken(string token)
    {
        Assert.Null(IdTokenAccountReader.Read(token));
    }

    [Fact]
    public void Read_ReturnsNull_ForNonObjectPayload()
    {
        Assert.Null(IdTokenAccountReader.Read(CreateToken("[1,2]")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Read_ReturnsNull_WhenTokenMissing(string? token)
    {
        Assert.Null(IdTokenAccountReader.Read(token));
    }

    private static string CreateToken(string payloadJson)
    {
        static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        return Encode("""{"alg":"none"}""") + "." + Encode(payloadJson) + ".signature";
    }
}
