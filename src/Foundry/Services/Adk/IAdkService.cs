// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

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

    /// <summary>Gets the last installation outcome, including restart and recovery requirements.</summary>
    AdkInstallResult? LastResult { get; }

    /// <summary>Gets the active orchestration or independently owned native wait for safe application close.</summary>
    Task? ActiveOperation { get; }

    /// <summary>Gets whether machine ownership must be reconciled before further operations.</summary>
    bool HasUncertainOwnership { get; }

    /// <summary>Requests cancellation of further setup stages and independently joins owned native completion.</summary>
    Task RequestCancellationAndWaitAsync(CancellationToken waitToken);

    /// <summary>Restores admission after the application abandons a close request.</summary>
    void ResumeAfterCancelledClose();

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
    Task<AdkInstallResult> InstallAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Upgrades installed ADK components when the current versions are unsupported.
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the upgrade operation.</param>
    /// <returns>The installation status after the operation.</returns>
    Task<AdkInstallResult> UpgradeAsync(CancellationToken cancellationToken = default);
}
