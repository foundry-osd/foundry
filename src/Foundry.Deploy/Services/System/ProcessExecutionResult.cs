// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.System;

public sealed record ProcessExecutionResult
{
    public int ExitCode { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string Arguments { get; init; } = string.Empty;
    public string WorkingDirectory { get; init; } = string.Empty;
    public string StandardOutput { get; init; } = string.Empty;
    public string StandardError { get; init; } = string.Empty;

    public bool IsSuccess => ExitCode == 0;
}
