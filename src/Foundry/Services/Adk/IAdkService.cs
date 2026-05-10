using Foundry.Core.Services.Adk;

namespace Foundry.Services.Adk;

public interface IAdkService
{
    event EventHandler<AdkStatusChangedEventArgs>? StatusChanged;
    AdkInstallationStatus CurrentStatus { get; }
    Task<AdkInstallationStatus> RefreshStatusAsync(CancellationToken cancellationToken = default);
    Task<AdkInstallationStatus> InstallAsync(CancellationToken cancellationToken = default);
    Task<AdkInstallationStatus> UpgradeAsync(CancellationToken cancellationToken = default);
}
