using Foundry.Core.Services.Adk;

namespace Foundry.Services.Adk;

/// <summary>
/// Carries the latest Windows ADK installation status to subscribers.
/// </summary>
/// <param name="status">Current ADK installation status.</param>
public sealed class AdkStatusChangedEventArgs(AdkInstallationStatus status) : EventArgs
{
    /// <summary>
    /// Gets the current ADK installation status.
    /// </summary>
    public AdkInstallationStatus Status { get; } = status;
}
