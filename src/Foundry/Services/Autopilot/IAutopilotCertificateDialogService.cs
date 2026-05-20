namespace Foundry.Services.Autopilot;

/// <summary>
/// Shows Autopilot certificate dialogs that require richer selectable content.
/// </summary>
public interface IAutopilotCertificateDialogService
{
    /// <summary>
    /// Shows the one-time generated PFX password after certificate creation.
    /// </summary>
    /// <param name="pfxOutputPath">Operator-selected PFX output path.</param>
    /// <param name="password">Generated PFX password.</param>
    Task ShowCreatedAsync(string pfxOutputPath, string password);
}
