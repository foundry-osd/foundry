// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Utilities.Processes;

/// <summary>
/// Preserves captured process diagnostics when cancellation interrupts execution.
/// A known exit code confirms only the root process exit, not its descendants or external side effects.
/// </summary>
public sealed class ProcessCanceledException : OperationCanceledException
{
    /// <summary>Creates a cancellation outcome without including raw output in the exception message.</summary>
    public ProcessCanceledException(
        ProcessExecutionRequest request,
        string arguments,
        string standardOutput,
        string standardError,
        int? exitCode,
        OperationCanceledException innerException,
        CancellationToken cancellationToken)
        : base("External process execution was canceled.", innerException, cancellationToken)
    {
        FileName = request.FileName;
        Arguments = arguments;
        WorkingDirectory = request.WorkingDirectory;
        StandardOutput = standardOutput;
        StandardError = standardError;
        ExitCode = exitCode;
    }

    /// <summary>Gets the executable whose execution was interrupted.</summary>
    public string FileName { get; }

    /// <summary>Gets command arguments for local diagnostics; remote callers must filter them.</summary>
    public string Arguments { get; }

    /// <summary>Gets the process working directory for local diagnostics.</summary>
    public string WorkingDirectory { get; }

    /// <summary>Gets output captured before the cancellation snapshot.</summary>
    public string StandardOutput { get; }

    /// <summary>Gets error output captured before the cancellation snapshot.</summary>
    public string StandardError { get; }

    /// <summary>Gets the root exit code when termination was confirmed within the bounded wait.</summary>
    public int? ExitCode { get; }

    /// <summary>Indicates root process termination only; cleanup may still be unresolved.</summary>
    public bool ProcessExitConfirmed => ExitCode.HasValue;
}
