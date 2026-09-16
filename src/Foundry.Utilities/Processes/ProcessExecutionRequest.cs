// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Utilities.Processes;

/// <summary>
/// Describes a process execution without imposing application logging or policy.
/// </summary>
public sealed record ProcessExecutionRequest
{
    private ProcessExecutionRequest(string fileName, string rawArguments, string workingDirectory)
    {
        FileName = fileName;
        RawArguments = rawArguments;
        WorkingDirectory = workingDirectory;
    }

    /// <summary>
    /// Initializes a request that passes each argument as an independent token.
    /// </summary>
    public ProcessExecutionRequest(string fileName, IEnumerable<string> argumentList, string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(argumentList);

        string[] arguments = [.. argumentList];
        if (arguments.Any(static argument => argument is null))
        {
            throw new ArgumentException("Argument values cannot be null.", nameof(argumentList));
        }

        FileName = fileName;
        ArgumentList = arguments;
        WorkingDirectory = workingDirectory;
    }

    /// <summary>
    /// Gets the executable path or name.
    /// </summary>
    public string FileName { get; }

    /// <summary>
    /// Gets the independent argument tokens, or <see langword="null"/> for a raw request.
    /// </summary>
    public IReadOnlyList<string>? ArgumentList { get; }

    /// <summary>
    /// Gets the caller-supplied raw argument string, or <see langword="null"/> for a tokenized request.
    /// </summary>
    public string? RawArguments { get; }

    /// <summary>
    /// Gets the process working directory.
    /// </summary>
    public string WorkingDirectory { get; }

    /// <summary>
    /// Gets environment values to set or remove. A <see langword="null"/> value removes the variable.
    /// </summary>
    public IReadOnlyDictionary<string, string?>? EnvironmentOverrides { get; init; }

    /// <summary>
    /// Gets a callback invoked for each standard-output line.
    /// </summary>
    public Action<string>? OnOutputData { get; init; }

    /// <summary>
    /// Gets a callback invoked for each standard-error line.
    /// </summary>
    public Action<string>? OnErrorData { get; init; }

    internal bool UsesRawArguments => RawArguments is not null;

    /// <summary>
    /// Creates a request that explicitly passes a raw argument string to the executable.
    /// </summary>
    public static ProcessExecutionRequest FromRawArguments(
        string fileName,
        string rawArguments,
        string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(rawArguments);
        return new ProcessExecutionRequest(fileName, rawArguments, workingDirectory);
    }
}
