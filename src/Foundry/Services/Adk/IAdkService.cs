using Foundry.Core.Services.Adk;

namespace Foundry.Services.Adk;

/// <summary>
/// Manages Windows ADK and WinPE add-on readiness for media creation workflows.
/// </summary>
public interface IAdkService
{
    /// <summary>
    /// Occurs when the detected ADK installation status changes.
    /// </summary>
    event EventHandler<AdkStatusChangedEventArgs>? StatusChanged;

    /// <summary>
    /// Gets the latest detected ADK installation status.
    /// </summary>
    AdkInstallationStatus CurrentStatus { get; }

    /// <summary>
    /// Re-detects installed ADK components and publishes the resulting status.
    /// </summary>
    /// <param name="cancellationToken">Token that cancels detection.</param>
    /// <returns>The refreshed installation status.</returns>
    Task<AdkInstallationStatus> RefreshStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Installs missing ADK components required by Foundry media creation.
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the install operation.</param>
    /// <returns>The installation status after the operation.</returns>
    Task<AdkInstallationStatus> InstallAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Upgrades installed ADK components when the current versions are unsupported.
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the upgrade operation.</param>
    /// <returns>The installation status after the operation.</returns>
    Task<AdkInstallationStatus> UpgradeAsync(CancellationToken cancellationToken = default);
}
