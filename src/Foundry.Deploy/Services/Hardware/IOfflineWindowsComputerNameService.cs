namespace Foundry.Deploy.Services.Hardware;

public interface IOfflineWindowsComputerNameService
{
    /// <summary>
    /// Scans all available drive letters for an existing Windows installation and returns
    /// the computer name stored in its offline SYSTEM registry hive.
    /// Returns <c>null</c> if no Windows installation is found or the name cannot be read.
    /// </summary>
    Task<string?> TryGetOfflineComputerNameAsync(CancellationToken cancellationToken = default);
}
