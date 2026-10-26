// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Utilities.Networking;

namespace Foundry.Utilities.Tests.Networking;

public sealed class SmbConnectorTests
{
    [Theory]
    [InlineData(0, SmbConnectStatus.Connected)]
    [InlineData(5, SmbConnectStatus.AccessDenied)]
    [InlineData(1326, SmbConnectStatus.AccessDenied)]
    [InlineData(53, SmbConnectStatus.NotFound)]
    [InlineData(67, SmbConnectStatus.NotFound)]
    [InlineData(1219, SmbConnectStatus.CredentialConflict)]
    [InlineData(1231, SmbConnectStatus.Failed)]
    public void Map_ClassifiesWin32Errors(int error, SmbConnectStatus expected)
    {
        SmbConnectResult result = SmbConnector.Map(error);

        Assert.Equal(expected, result.Status);
        Assert.Equal(error, result.Win32Error);
    }
}
