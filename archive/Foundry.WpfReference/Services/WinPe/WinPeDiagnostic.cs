namespace Foundry.Services.WinPe;

public sealed record WinPeDiagnostic(
    string Code,
    string Message,
    string? Details = null,
    string? Stage = null,
    string? Command = null,
    int? ExitCode = null);
