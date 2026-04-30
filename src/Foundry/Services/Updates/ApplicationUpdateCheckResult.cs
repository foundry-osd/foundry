namespace Foundry.Services.Updates;

public sealed record ApplicationUpdateCheckResult(
    ApplicationUpdateStatus Status,
    string Message,
    string? Version = null,
    string? ReleaseNotes = null)
{
    public bool IsUpdateAvailable => Status == ApplicationUpdateStatus.UpdateAvailable;
}
