// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Utilities.Processes;

namespace Foundry.Core.Services.WinPe;

public sealed record WinPeProcessExecution
{
    private const int DismAccessDeniedExitCode = 5;

    private static readonly string[] CopypeBlockedFileAccessMarkers =
    [
        "Failed to mount the WinPE WIM file",
        "Unable to copy boot file"
    ];

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

    /// <summary>
    /// Converts a failed process execution into a diagnostic. Failures that show Windows denied access to
    /// WinPE image files (typically security software or another tool locking them) are reported with
    /// <see cref="WinPeFailureReasons.AccessDenied"/> instead of <see cref="WinPeFailureReasons.NonZeroExit"/>.
    /// </summary>
    public WinPeDiagnostic ToFailureDiagnostic(
        string code,
        string message,
        string? stage = null,
        string? toolName = null)
    {
        string resolvedToolName = toolName ?? Path.GetFileNameWithoutExtension(FileName);
        return new WinPeDiagnostic(
            code,
            message,
            ToDiagnosticText(),
            stage,
            exitCode: ExitCode,
            failureKind: WinPeFailureKinds.Process,
            failureReason: IsFileAccessBlocked(resolvedToolName)
                ? WinPeFailureReasons.AccessDenied
                : WinPeFailureReasons.NonZeroExit,
            toolName: resolvedToolName);
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

    /// <summary>
    /// Detects blocked file access from stable signals only, because DISM and copype stderr text is localized:
    /// DISM exit code 5 (ERROR_ACCESS_DENIED) and the English markers echoed by the ADK copype.cmd script.
    /// </summary>
    private bool IsFileAccessBlocked(string toolName)
    {
        string normalizedToolName = Path.GetFileNameWithoutExtension(toolName);
        if (string.Equals(normalizedToolName, "dism", StringComparison.OrdinalIgnoreCase))
        {
            return ExitCode == DismAccessDeniedExitCode;
        }

        return string.Equals(normalizedToolName, "copype", StringComparison.OrdinalIgnoreCase) &&
            CopypeBlockedFileAccessMarkers.Any(marker =>
                StandardOutput.Contains(marker, StringComparison.OrdinalIgnoreCase) ||
                StandardError.Contains(marker, StringComparison.OrdinalIgnoreCase));
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
