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

    [Fact]
    public void ReadErrorText_WhenStreamIsClixml_ReturnsOnlyErrorRecordText()
    {
        const string standardError = """
            #< CLIXML
            <Objs Version="1.1.0.1" xmlns="http://schemas.microsoft.com/powershell/2004/04"><Obj S="progress" RefId="0"><TN RefId="0"><T>System.Management.Automation.PSCustomObject</T><T>System.Object</T></TN><MS><I64 N="SourceId">1</I64><PR N="Record"><AV>Preparing modules for first use.</AV><AI>0</AI><Nil /><PI>-1</PI><PC>-1</PC><T>Completed</T><SR>-1</SR><SD> </SD></PR></MS></Obj><S S="Error">Get-Disk : Access to a CIM resource was not available to the client. _x000D__x000A_</S><S S="Error">At line:1 char:10_x000D__x000A_</S><S S="Error">    + CategoryInfo          : PermissionDenied: (MSFT_Disk:ROOT/Microsoft/Windows/Storage/MSFT_Disk) [Get-Disk], &lt;CimException&gt;_x000D__x000A_</S></Objs>
            """;

        string errorText = PowerShellCommand.ReadErrorText(standardError);

        Assert.Equal(
            string.Join(
                Environment.NewLine,
                "Get-Disk : Access to a CIM resource was not available to the client.",
                "At line:1 char:10",
                "    + CategoryInfo          : PermissionDenied: (MSFT_Disk:ROOT/Microsoft/Windows/Storage/MSFT_Disk) [Get-Disk], <CimException>"),
            errorText);
    }

    [Theory]
    [InlineData("  The term 'Get-Disk' is not recognized.\r\n", "The term 'Get-Disk' is not recognized.")]
    [InlineData("", "")]
    [InlineData("#< CLIXML\r\n<Objs Version=\"1.1.0.1\"><Obj S=\"progress\" RefId=\"0\" /></Objs>", "")]
    public void ReadErrorText_WhenStreamHasNoErrorRecords_ReturnsTrimmedPlainText(string standardError, string expected)
    {
        Assert.Equal(expected, PowerShellCommand.ReadErrorText(standardError));
    }

    [Fact]
    public void ReadErrorText_WhenStreamIsVeryLong_KeepsTheFirstErrorsAndMarksTruncation()
    {
        string errorText = PowerShellCommand.ReadErrorText("first failure" + new string('x', 10_000));

        Assert.StartsWith("first failure", errorText, StringComparison.Ordinal);
        Assert.EndsWith("<truncated>", errorText, StringComparison.Ordinal);
        Assert.True(errorText.Length < 5_000);
    }
}
