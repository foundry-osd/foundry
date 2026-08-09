// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Utilities.Processes;

namespace Foundry.Core.Services.WinPe;

public sealed record WinPeProcessExecution
{
    public int ExitCode { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string Arguments { get; init; } = string.Empty;
    public string WorkingDirectory { get; init; } = string.Empty;
    public string StandardOutput { get; init; } = string.Empty;
    public string StandardError { get; init; } = string.Empty;

    public bool IsSuccess => ExitCode == 0;

    public string ToDiagnosticText()
    {
        return ToProcessExecutionResult().ToDiagnosticText();
    }

    internal static WinPeProcessExecution FromProcessExecutionResult(ProcessExecutionResult result)
    {
        return new WinPeProcessExecution
        {
            ExitCode = result.ExitCode,
            FileName = result.FileName,
            Arguments = result.Arguments,
            WorkingDirectory = result.WorkingDirectory,
            StandardOutput = result.StandardOutput,
            StandardError = result.StandardError
        };
    }

    private ProcessExecutionResult ToProcessExecutionResult()
    {
        return new ProcessExecutionResult
        {
            ExitCode = ExitCode,
            FileName = FileName,
            Arguments = Arguments,
            WorkingDirectory = WorkingDirectory,
            StandardOutput = StandardOutput,
            StandardError = StandardError
        };
    }
}
