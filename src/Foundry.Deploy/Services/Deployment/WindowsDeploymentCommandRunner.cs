// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Services.Logging;
using Foundry.Deploy.Services.System;
using Foundry.Utilities.Processes;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.Services.Deployment;

/// <summary>Preserves bounded native invocation and safe error reporting across Windows servicing owners.</summary>
internal sealed class WindowsDeploymentCommandRunner(IProcessRunner processRunner, ILogger logger)
{
    public Task<ProcessExecutionResult> RunRequiredProcessAsync(string fileName, IEnumerable<string> arguments,
        string workingDirectory, string failureSummary, CancellationToken cancellationToken,
        TimeSpan? executionTimeout = null) => RunRequiredProcessAsync(fileName, arguments, workingDirectory,
            failureSummary, cancellationToken, null, null, executionTimeout);

    public async Task<ProcessExecutionResult> RunRequiredProcessAsync(string fileName, IEnumerable<string> arguments,
        string workingDirectory, string failureSummary, CancellationToken cancellationToken,
        Action<string>? onOutputData, Action<string>? onErrorData, TimeSpan? executionTimeout = null)
    {
        ProcessExecutionResult execution = await processRunner.RunAsync(fileName, arguments, workingDirectory,
            onOutputData, onErrorData, cancellationToken, executionTimeout ?? TimeSpan.FromHours(4)).ConfigureAwait(false);
        if (!execution.IsSuccess)
        {
            string diagnostic = VolumePathDiagnostics.Redact(execution.ToDiagnosticText());
            logger.LogError("{FailureSummary}. Diagnostic={Diagnostic}", failureSummary, diagnostic);
            throw new DeploymentProcessException($"{failureSummary}.{Environment.NewLine}{diagnostic}", execution.ExitCode);
        }
        return execution;
    }
}
