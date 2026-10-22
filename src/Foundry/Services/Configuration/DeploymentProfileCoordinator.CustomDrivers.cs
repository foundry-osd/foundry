// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Drivers;

namespace Foundry.Services.Configuration;

public sealed partial class DeploymentProfileCoordinator
{
    /// <summary>
    /// Removes a driver set and deletes its cached package only after saving and checking the driver set references of all local profiles.
    /// Removing the default set clears the default.
    /// </summary>
    /// <returns>False when the set was removed but package cleanup could not safely complete.</returns>
    public async Task<bool> RemoveCustomDriverSetAsync(CustomDriverSetsSettings expected, string setId, CustomDriverSetLibrary library)
    {
        using var suspension = SuspendActivation();
        await gate.WaitAsync(lifetime.Token);
        try
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
                long version = editVersion;
                CustomDriverSetsSettings remaining = configuration.Current.CustomDrivers;
                if (Active is not null && editVersion != persistedEditVersion) await SaveCurrentAsync();
                using var references = await Task.Run(local.AcquireCustomDriverSetPackageReferences, lifetime.Token);
                if (editVersion != version || !ReferenceEquals(remaining, configuration.Current.CustomDrivers))
                    throw new InvalidOperationException("The configuration changed during package cleanup.");

                var hashes = new HashSet<string>(references.ContentHashes, StringComparer.OrdinalIgnoreCase);
                foreach (CustomDriverSetReference item in remaining.Sets)
                    hashes.Add(item.Package.ContentHash);
                if (hashes.Contains(contentHash))
                {
                    Logger.Information("Custom driver set package retained because it is referenced. ContentHash={ContentHash}", contentHash);
                    return true;
                }

                await library.DeleteAsync(contentHash, hashes, lifetime.Token);
                Logger.Information("Custom driver set package cleanup completed. ContentHash={ContentHash}", contentHash);
                return true;
            }
            catch (Exception exception)
            {
                Logger.Warning(exception, "Custom driver set removed but package cleanup could not complete. SetId={SetId}, ContentHash={ContentHash}", setId, contentHash);
                return false;
            }
        }
        finally { gate.Release(); }
    }
}
