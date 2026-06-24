// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;

namespace Foundry.Deploy.Services.System;

public interface IProcessRunner
{
    Task<ProcessExecutionResult> RunAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        CancellationToken cancellationToken = default);

    Task<ProcessExecutionResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken = default);

    Task<ProcessExecutionResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        Action<string>? onOutputData,
        Action<string>? onErrorData,
        CancellationToken cancellationToken = default);
}
