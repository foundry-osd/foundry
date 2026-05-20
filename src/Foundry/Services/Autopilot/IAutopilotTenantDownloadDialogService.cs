using Foundry.Core.Models.Configuration;

namespace Foundry.Services.Autopilot;

/// <summary>
/// Shows the tenant profile download dialog and coordinates cancellation with the download operation.
/// </summary>
public interface IAutopilotTenantDownloadDialogService
{
    /// <summary>
    /// Runs a tenant Graph operation through the shared tenant sign-in dialog.
    /// </summary>
    /// <typeparam name="TResult">Operation result type.</typeparam>
    /// <param name="title">Dialog title.</param>
    /// <param name="message">Dialog message.</param>
    /// <param name="operationAsync">Delegate that runs using the dialog-owned cancellation token.</param>
    /// <returns>The operation result, or <see langword="null"/> when the dialog is canceled.</returns>
    Task<TResult?> RunAsync<TResult>(
        string title,
        string message,
        Func<CancellationToken, Task<TResult>> operationAsync)
        where TResult : class;

    /// <summary>
    /// Runs a profile download through a modal dialog.
    /// </summary>
    /// <param name="downloadProfilesAsync">Delegate that downloads profiles using the dialog-owned cancellation token.</param>
    /// <returns>Downloaded profiles, or <see langword="null"/> when the dialog is canceled.</returns>
    Task<IReadOnlyList<AutopilotProfileSettings>?> DownloadAsync(
        Func<CancellationToken, Task<IReadOnlyList<AutopilotProfileSettings>>> downloadProfilesAsync);
}
