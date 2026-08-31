// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
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

        try
        {
            return await _processRunner.RunAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
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
