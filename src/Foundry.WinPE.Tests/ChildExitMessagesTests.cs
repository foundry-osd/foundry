// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Xunit;

namespace Foundry.WinPE.Tests;

public sealed class ChildExitMessagesTests
{
    [Theory]
    [InlineData(21, "could not start")]
    [InlineData(22, "Recreate the boot media")]
    [InlineData(23, "stopped unexpectedly")]
    public void Describe_ConnectCodes_AreSpecific(int code, string expected)
        => Assert.Contains(expected, ChildExitMessages.Describe(code));

    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    [InlineData(-1)]
    public void Describe_OtherCodes_DescribeTheUiProcessAsFoundry(int code)
        => Assert.Equal($"Foundry stopped unexpectedly with exit code {code}. Check the session log for details.",
            ChildExitMessages.Describe(code));
}
