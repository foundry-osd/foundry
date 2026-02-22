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
