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

        string diagnostic = execution.ToDiagnosticText();

        Assert.Contains("Command: dism.exe /?", diagnostic, StringComparison.Ordinal);
        Assert.Contains("WorkingDirectory: C:\\Work", diagnostic, StringComparison.Ordinal);
        Assert.Contains("ExitCode: 1", diagnostic, StringComparison.Ordinal);
        Assert.Contains("output", diagnostic, StringComparison.Ordinal);
        Assert.Contains("error", diagnostic, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("C:\\Tools\\copype.cmd", "C:\\Tools\\copype.cmd")]
    [InlineData("C:\\Program Files\\copype.cmd", "\"C:\\Program Files\\copype.cmd\"")]
    public void Quote_QuotesOnlyWhenValueContainsSpaces(string value, string expected)
    {
        Assert.Equal(expected, WinPeProcessRunner.Quote(value));
    }
}
