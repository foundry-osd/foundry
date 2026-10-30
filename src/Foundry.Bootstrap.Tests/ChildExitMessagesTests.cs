// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Xunit;

namespace Foundry.Bootstrap.Tests;

public sealed class ChildExitMessagesTests
{
    [Theory]
    [InlineData(21, "could not start")]
    [InlineData(22, "Recreate the boot media")]
    [InlineData(23, "stopped unexpectedly")]
    public void Describe_ConnectCodes_AreSpecific(int code, string expected)
        => Assert.Contains(expected, ChildExitMessages.Describe(BootstrapStage.Connect, code));

    [Fact]
    public void Describe_UnknownCode_UsesGenericMessage()
        => Assert.Equal("Foundry Connect stopped with exit code 7.", ChildExitMessages.Describe(BootstrapStage.Connect, 7));

    [Fact]
    public void Describe_DeployStage_UsesGenericMessageEvenForConnectCodes()
        => Assert.Equal("Foundry Deploy stopped with exit code 21.", ChildExitMessages.Describe(BootstrapStage.Deploy, 21));
}
