// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Diagnostics;
using Foundry.Telemetry;
using Foundry.Utilities.Processes;
using Microsoft.Extensions.Logging;

namespace Foundry.Connect.Services.System;

internal sealed class ConnectProcessExecutor(ILogger logger)
{
    private readonly ProcessRunner _processRunner = new();

    public async Task<ProcessExecutionResult> ExecuteAsync(
        string fileName,
        string arguments,
        CancellationToken cancellationToken)
    {
        ProcessExecutionRequest request = ProcessExecutionRequest.FromRawArguments(
            fileName,
            arguments,
            Environment.CurrentDirectory);

        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            ProcessExecutionResult result = await _processRunner.RunAsync(request, cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                using IDisposable? diagnosticScope = logger.BeginScope(RemoteProcessDiagnostics.CreateProperties(result, stopwatch.Elapsed));
                logger.LogWarning("External process returned a nonzero exit code. ToolName={ToolName}, ExitCode={ExitCode}",
                    Path.GetFileName(result.FileName), result.ExitCode);
            }
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (ex is ProcessStartException startException)
            {
                using IDisposable? diagnosticScope = logger.BeginScope(RemoteProcessDiagnostics.CreateStartFailureProperties(startException, stopwatch.Elapsed));
                logger.LogWarning(ex, "External process could not start.");
            }
            logger.LogDebug(
                ex,
                "Process execution failed. FileName={FileName}, FailureType={FailureType}",
                Path.GetFileName(fileName),
                ex.GetType().Name);
            return new ProcessExecutionResult
            {
                ExitCode = -1,
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = Environment.CurrentDirectory,
                StandardError = ex.Message
            };
        }
    }
}
