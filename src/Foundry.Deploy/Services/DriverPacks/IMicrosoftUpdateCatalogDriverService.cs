using Foundry.Deploy.Models;

namespace Foundry.Deploy.Services.DriverPacks;

public interface IMicrosoftUpdateCatalogDriverService
{
    Task<MicrosoftUpdateCatalogDriverResult> DownloadAsync(
        HardwareProfile hardwareProfile,
        OperatingSystemCatalogItem operatingSystem,
        string destinationDirectory,
        CancellationToken cancellationToken = default,
        IProgress<double>? progress = null);

    Task<MicrosoftUpdateCatalogDriverResult> ExpandAsync(
        string sourceDirectory,
        string destinationDirectory,
        CancellationToken cancellationToken = default,
        IProgress<double>? progress = null);
}
