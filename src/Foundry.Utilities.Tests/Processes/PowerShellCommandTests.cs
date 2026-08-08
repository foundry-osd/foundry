// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Utilities.Processes;

namespace Foundry.Utilities.Tests.Processes;

public sealed class PowerShellCommandTests
{
    [Fact]
    public void CreateEncodedArguments_ReturnsIndependentTokensWithUtf16LittleEndianPayload()
    {
        IReadOnlyList<string> arguments = PowerShellCommand.CreateEncodedArguments("Write-Output 'café'");

        Assert.Equal(
            [
                "-EncodedCommand",
                "VwByAGkAdABlAC0ATwB1AHQAcAB1AHQAIAAnAGMAYQBmAOkAJwA="
            ],
            arguments);
    }
}
