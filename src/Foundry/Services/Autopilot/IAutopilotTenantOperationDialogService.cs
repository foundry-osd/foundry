namespace Foundry.Services.Autopilot;

/// <summary>
/// Shows the shared tenant Microsoft Graph operation dialog and coordinates cancellation with the operation.
/// </summary>
public interface IAutopilotTenantOperationDialogService
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
}
