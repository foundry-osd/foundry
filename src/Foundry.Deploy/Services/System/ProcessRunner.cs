using System.Diagnostics;
using System.Linq;
using System.Text;
using System.IO;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.Services.System;

public sealed class ProcessRunner : IProcessRunner
{
    private readonly ILogger<ProcessRunner> _logger;

    public ProcessRunner(ILogger<ProcessRunner> logger)
    {
        _logger = logger;
    }

    public async Task<ProcessExecutionResult> RunAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        return await RunAsyncCore(
            fileName,
            workingDirectory,
            startInfo => startInfo.Arguments = arguments,
            arguments,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProcessExecutionResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        List<string> argumentList = [.. arguments];
        string argumentsDisplay = string.Join(
            " ",
            argumentList.Select(static argument => argument.Any(char.IsWhiteSpace)
                ? $"\"{argument}\""
                : argument));

        return await RunAsyncCore(
            fileName,
            workingDirectory,
            startInfo =>
            {
                foreach (string argument in argumentList)
                {
                    startInfo.ArgumentList.Add(argument);
                }
            },
            argumentsDisplay,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<ProcessExecutionResult> RunAsyncCore(
        string fileName,
        string workingDirectory,
        Action<ProcessStartInfo> configureArguments,
        string argumentsDisplay,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("Executable path is required.", nameof(fileName));
        }

        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            throw new ArgumentException("Working directory is required.", nameof(workingDirectory));
        }

        Directory.CreateDirectory(workingDirectory);
        _logger.LogDebug("Starting process. FileName={FileName}, Arguments={Arguments}, WorkingDirectory={WorkingDirectory}",
            fileName,
            argumentsDisplay,
            workingDirectory);

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        configureArguments(startInfo);

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
            }
        };

        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                stderrBuilder.AppendLine(args.Data);
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Unable to start process '{fileName}'.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var registration = cancellationToken.Register(() =>
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
                // Best effort cancellation.
            }
        });

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        ProcessExecutionResult result = new()
        {
            ExitCode = process.ExitCode,
            FileName = fileName,
            Arguments = argumentsDisplay,
            WorkingDirectory = workingDirectory,
            StandardOutput = stdoutBuilder.ToString(),
            StandardError = stderrBuilder.ToString()
        };

        _logger.LogDebug("Process completed. FileName={FileName}, ExitCode={ExitCode}", fileName, result.ExitCode);
        return result;
    }
}
