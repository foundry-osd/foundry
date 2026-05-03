using Foundry.Core.Services.Adk;

namespace Foundry.Services.Adk;

public sealed class AdkStatusChangedEventArgs(AdkInstallationStatus status) : EventArgs
{
    public AdkInstallationStatus Status { get; } = status;
}
