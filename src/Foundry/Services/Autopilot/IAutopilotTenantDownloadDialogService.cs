using Foundry.Core.Models.Configuration;

namespace Foundry.Services.Autopilot;

/// <summary>
/// Shows the tenant profile download dialog and coordinates cancellation with the download operation.
/// </summary>
public interface IAutopilotTenantDownloadDialogService
{
    /// <summary>
    /// Runs a profile download through a modal dialog.
    /// </summary>
    /// <param name="downloadProfilesAsync">Delegate that downloads profiles using the dialog-owned cancellation token.</param>
    /// <returns>Downloaded profiles, or <see langword="null"/> when the dialog is canceled.</returns>
    Task<IReadOnlyList<AutopilotProfileSettings>?> DownloadAsync(
        Func<CancellationToken, Task<IReadOnlyList<AutopilotProfileSettings>>> downloadProfilesAsync);
}
