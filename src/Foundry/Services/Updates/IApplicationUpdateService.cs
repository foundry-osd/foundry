namespace Foundry.Services.Updates;

public interface IApplicationUpdateService
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<ApplicationUpdateCheckResult> CheckForUpdatesAsync(bool isStartupCheck = false, CancellationToken cancellationToken = default);
    Task<ApplicationUpdateDownloadResult> DownloadUpdateAsync(IProgress<int>? progress = null, CancellationToken cancellationToken = default);
    void ApplyUpdateAndRestart();
}
