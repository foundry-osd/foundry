// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Foundry.Utilities.Processes;

/// <summary>
/// Runs a process with redirected UTF-8 output and cancellation-aware tree termination.
/// </summary>
public sealed class ProcessRunner
{
    /// <summary>
    /// Runs a process and captures its output.
    /// </summary>
    public async Task<ProcessExecutionResult> RunAsync(
        ProcessExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.FileName))
        {
            throw new ArgumentException("Executable path is required.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.WorkingDirectory))
        {
            throw new ArgumentException("Working directory is required.", nameof(request));
        }

        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(request.WorkingDirectory);

        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            WorkingDirectory = request.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        string argumentsDisplay;
        if (request.UsesRawArguments)
        {
            startInfo.Arguments = request.RawArguments!;
            argumentsDisplay = request.RawArguments!;
        }
        else
        {
            IReadOnlyList<string> arguments = request.ArgumentList ?? [];
            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            argumentsDisplay = string.Join(" ", arguments.Select(FormatArgumentForDisplay));
        }

        ApplyEnvironmentOverrides(startInfo, request.EnvironmentOverrides);

        using var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };
        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();

        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                stdoutBuilder.AppendLine(args.Data);
                InvokeCallback(request.OnOutputData, args.Data);
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                stderrBuilder.AppendLine(args.Data);
                InvokeCallback(request.OnErrorData, args.Data);
            }
        };

        Start(process, request.FileName);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var registration = cancellationToken.Register(static state => TryKill((Process)state!), process);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            await WaitForTerminationAsync(process).ConfigureAwait(false);
            throw;
        }

        return new ProcessExecutionResult
        {
            ExitCode = process.ExitCode,
            FileName = request.FileName,
            Arguments = argumentsDisplay,
            WorkingDirectory = request.WorkingDirectory,
            StandardOutput = stdoutBuilder.ToString(),
            StandardError = stderrBuilder.ToString()
        };
    }

    private static void ApplyEnvironmentOverrides(
        ProcessStartInfo startInfo,
        IReadOnlyDictionary<string, string?>? environmentOverrides)
    {
        if (environmentOverrides is null)
        {
            return;
        }

        foreach ((string name, string? value) in environmentOverrides)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Environment variable names cannot be blank.", nameof(environmentOverrides));
            }

            if (value is null)
            {
                startInfo.Environment.Remove(name);
            }
            else
            {
                startInfo.Environment[name] = value;
            }
        }
    }

    private static string FormatArgumentForDisplay(string argument)
    {
        if (argument.Length == 0)
        {
            return "\"\"";
        }

        if (!argument.Any(static character => char.IsWhiteSpace(character) || character == '"'))
        {
            return argument;
        }

        var builder = new StringBuilder(argument.Length + 2);
        builder.Append('"');

        int pendingBackslashes = 0;
        foreach (char character in argument)
        {
            if (character == '\\')
            {
                pendingBackslashes++;
                continue;
            }

            if (character == '"')
            {
                builder.Append('\\', (pendingBackslashes * 2) + 1);
                builder.Append(character);
            }
            else
            {
                builder.Append('\\', pendingBackslashes);
                builder.Append(character);
            }

            pendingBackslashes = 0;
        }

        builder.Append('\\', pendingBackslashes * 2);
        builder.Append('"');
        return builder.ToString();
    }

    private static void InvokeCallback(Action<string>? callback, string data)
    {
        try
        {
            callback?.Invoke(data);
        }
        catch
        {
            // Output callbacks cannot affect process execution or capture.
        }
    }

    private static void Start(Process process, string fileName)
    {
        try
        {
            if (!process.Start())
            {
                throw new ProcessStartException(fileName, $"Unable to start process '{fileName}'.");
            }
        }
        catch (ProcessStartException)
        {
            throw;
        }
        catch (Win32Exception ex)
        {
            throw new ProcessStartException(
                fileName,
                $"Unable to start process '{fileName}'.",
                ex.NativeErrorCode,
                ex);
        }
        catch (InvalidOperationException ex)
        {
            throw new ProcessStartException(
                fileName,
                $"Unable to start process '{fileName}'.",
                innerException: ex);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Process termination is best effort during cancellation.
        }
    }

    private static async Task WaitForTerminationAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // A concurrent exit can invalidate the process handle during cancellation.
        }
    }
}
