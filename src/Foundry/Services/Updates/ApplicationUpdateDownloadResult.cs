namespace Foundry.Services.Updates;

public sealed record ApplicationUpdateDownloadResult(
    ApplicationUpdateStatus Status,
    string Message);
