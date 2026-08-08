// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeProcessExecutionTests
{
    [Fact]
    public void ToDiagnosticText_IncludesCommandWorkingDirectoryExitCodeAndStreams()
    {
        var execution = new WinPeProcessExecution
        {
            FileName = "dism.exe",
            Arguments = "/?",
            WorkingDirectory = "C:\\Work",
            ExitCode = 1,
            StandardOutput = "output",
            StandardError = "error"
        };

        Assert.Equal(
            "Command: dism.exe /?\r\n" +
            "WorkingDirectory: C:\\Work\r\n" +
            "ExitCode: 1\r\n" +
            "StdOut:\r\n" +
            "output\r\n" +
            "StdErr:\r\n" +
            "error",
            execution.ToDiagnosticText());
    }

    [Theory]
    [InlineData("C:\\Tools\\copype.cmd", "C:\\Tools\\copype.cmd")]
    [InlineData("C:\\Program Files\\copype.cmd", "\"C:\\Program Files\\copype.cmd\"")]
    public void Quote_QuotesOnlyWhenValueContainsSpaces(string value, string expected)
    {
        Assert.Equal(expected, WinPeProcessRunner.Quote(value));
    }
}
