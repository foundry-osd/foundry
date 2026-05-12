namespace Foundry.Deploy.Services.Deployment;

/// <summary>
/// Manages Foundry-owned command blocks inside SetupComplete.cmd.
/// </summary>
public interface ISetupCompleteScriptService
{
    /// <summary>
    /// Adds a marked command block when it is not already present.
    /// </summary>
    /// <param name="setupCompletePath">Offline path to SetupComplete.cmd.</param>
    /// <param name="markerKey">Stable marker key used to identify the block.</param>
    /// <param name="scriptBody">Batch commands to place between the markers.</param>
    /// <returns>The SetupComplete.cmd path.</returns>
    string EnsureBlock(string setupCompletePath, string markerKey, string scriptBody);

    /// <summary>
    /// Removes a marked command block when it exists.
    /// </summary>
    /// <param name="setupCompletePath">Offline path to SetupComplete.cmd.</param>
    /// <param name="markerKey">Stable marker key used to identify the block.</param>
    /// <returns>The SetupComplete.cmd path.</returns>
    string RemoveBlock(string setupCompletePath, string markerKey);
}
