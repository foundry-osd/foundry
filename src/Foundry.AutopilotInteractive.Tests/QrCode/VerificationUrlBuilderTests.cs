// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.AutopilotInteractive.Services.QrCode;

namespace Foundry.AutopilotInteractive.Tests.QrCode;

public sealed class VerificationUrlBuilderTests
{
    [Fact]
    public void Build_AppendsEscapedOtc()
    {
        Uri url = VerificationUrlBuilder.Build(new Uri("https://microsoft.com/devicelogin"), "ABCD-EFGH");

        Assert.Equal("https://microsoft.com/devicelogin?otc=ABCD-EFGH", url.AbsoluteUri);
    }

    [Fact]
    public void Build_EscapesSpaces()
    {
        Uri url = VerificationUrlBuilder.Build(new Uri("https://microsoft.com/devicelogin"), "AB CD");

        Assert.Equal("https://microsoft.com/devicelogin?otc=AB%20CD", url.AbsoluteUri);
    }
}
