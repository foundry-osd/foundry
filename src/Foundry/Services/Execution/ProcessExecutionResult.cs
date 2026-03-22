using System.Text;

namespace Foundry.Services.Execution;

public sealed record ProcessExecutionResult
{
    public int ExitCode { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string Arguments { get; init; } = string.Empty;
    public string WorkingDirectory { get; init; } = string.Empty;
    public string StandardOutput { get; init; } = string.Empty;
    public string StandardError { get; init; } = string.Empty;

    public bool IsSuccess => ExitCode == 0;

    public string ToDiagnosticText()
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Command: {FileName} {Arguments}".TrimEnd());
        builder.AppendLine($"WorkingDirectory: {WorkingDirectory}");
        builder.AppendLine($"ExitCode: {ExitCode}");

        if (!string.IsNullOrWhiteSpace(StandardOutput))
        {
            builder.AppendLine("StdOut:");
            builder.AppendLine(StandardOutput.Trim());
        }

        if (!string.IsNullOrWhiteSpace(StandardError))
        {
            builder.AppendLine("StdErr:");
            builder.AppendLine(StandardError.Trim());
        }

        return builder.ToString().Trim();
    }
}
