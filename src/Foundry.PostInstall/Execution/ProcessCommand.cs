// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.PostInstall.Execution;

public sealed record ProcessCommand(string FileName, IReadOnlyList<string> Arguments,
    string WorkingDirectory, TimeSpan Timeout, string? RawArguments = null, string? OutputPath = null);

public sealed record ProcessOutcome(int? ExitCode, string StandardOutput, bool TimedOut = false,
    bool TerminationUncertain = false, bool OutputTruncated = false);

public interface IPreOobeProcessExecutor
{
    Task<ProcessOutcome> RunAsync(ProcessCommand command, CancellationToken cancellationToken);
}
