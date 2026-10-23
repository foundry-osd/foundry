// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.Drivers;
using Foundry.Core.Services.Packages;

namespace Foundry.Services.Configuration;

/// <summary>
/// Removes post-installation actions and custom driver sets from the current configuration, then deletes their library
/// content once the saved configuration no longer references it.
/// </summary>
/// <remarks>
/// The entry is always removed and persisted before any content is touched, so a failed cleanup never leaves a
/// configuration pointing at deleted content. The libraries refuse to delete leased content or content holding unknown
/// files, so a running media build or publication keeps what it reads.
/// </remarks>
public sealed class ContentPackageCleanupService(
    IFoundryConfigurationStateService configuration,
    ContentPackageLibraryService postInstallationLibrary,
    CustomDriverSetLibrary driverSetLibrary)
{
    private static readonly Serilog.ILogger Logger = Serilog.Log.ForContext<ContentPackageCleanupService>();

    /// <summary>Removes an action and deletes its package when no remaining action references it.</summary>
    /// <param name="expected">The post-installation section the caller confirmed the removal against.</param>
    /// <param name="actionId">The identifier of the action to remove.</param>
    /// <returns>False when the action was removed but package cleanup could not safely complete.</returns>
    /// <exception cref="InvalidOperationException">The post-installation section changed since <paramref name="expected"/> was read.</exception>
    public async Task<bool> RemovePostInstallationActionAsync(PreOobeSettings expected, string actionId)
    {
        if (!ReferenceEquals(expected, configuration.Current.PreOobe))
            throw new InvalidOperationException("The post-installation configuration changed before removal.");
        PreOobeActionSettings action = expected.Actions.Single(item => item.Id == actionId);
        configuration.UpdatePreOobe(expected with { Actions = expected.Actions.Where(item => item.Id != actionId).ToArray() }, requirePersistence: true);
        if (action.Package is not { } package) return true;

        Logger.Information("Post-installation package cleanup started. ActionId={ActionId}, ContentHash={ContentHash}", actionId, package.ContentHash);
        try
        {
            IReadOnlySet<string> references = ConfigurationContentReferences.PostInstallationPackageHashes(configuration.Current);
            if (references.Contains(package.ContentHash))
            {
                Logger.Information("Post-installation package retained because it is referenced. ContentHash={ContentHash}", package.ContentHash);
                return true;
            }

            await Task.Run(() => postInstallationLibrary.DeleteAsync(package.ContentHash, references));
            Logger.Information("Post-installation package cleanup completed. ContentHash={ContentHash}", package.ContentHash);
            return true;
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Post-installation action removed but package cleanup could not complete. ActionId={ActionId}, ContentHash={ContentHash}", actionId, package.ContentHash);
            return false;
        }
    }

    /// <summary>
    /// Removes a driver set and deletes its package when no remaining set references it. Removing the default set clears the default.
    /// </summary>
    /// <param name="expected">The custom driver section the caller confirmed the removal against.</param>
    /// <param name="setId">The identifier of the set to remove.</param>
    /// <returns>False when the set was removed but package cleanup could not safely complete.</returns>
    /// <exception cref="InvalidOperationException">The custom driver section changed since <paramref name="expected"/> was read.</exception>
    public async Task<bool> RemoveCustomDriverSetAsync(CustomDriverSetsSettings expected, string setId)
    {
        if (!ReferenceEquals(expected, configuration.Current.CustomDrivers))
            throw new InvalidOperationException("The custom driver set configuration changed before removal.");
        CustomDriverSetReference set = expected.Sets.Single(item => item.Id == setId);
        configuration.UpdateCustomDrivers(expected with
        {
            Sets = expected.Sets.Where(item => item.Id != setId).ToArray(),
            DefaultSetId = expected.DefaultSetId == setId ? null : expected.DefaultSetId
        }, requirePersistence: true);
        string contentHash = set.Package.ContentHash;

        Logger.Information("Custom driver set package cleanup started. SetId={SetId}, ContentHash={ContentHash}", setId, contentHash);
        try
        {
            IReadOnlySet<string> references = ConfigurationContentReferences.DriverSetHashes(configuration.Current);
            if (references.Contains(contentHash))
            {
                Logger.Information("Custom driver set package retained because it is referenced. ContentHash={ContentHash}", contentHash);
                return true;
            }

            await Task.Run(() => driverSetLibrary.DeleteAsync(contentHash, references));
            Logger.Information("Custom driver set package cleanup completed. ContentHash={ContentHash}", contentHash);
            return true;
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Custom driver set removed but package cleanup could not complete. SetId={SetId}, ContentHash={ContentHash}", setId, contentHash);
            return false;
        }
    }
}
