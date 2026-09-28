// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Packages;

namespace Foundry.Services.Configuration;

public sealed partial class DeploymentProfileCoordinator
{
    /// <summary>Removes an action and deletes its cached content only after saving and checking all local profile references.</summary>
    /// <returns>False when the action was removed but content cleanup could not safely complete.</returns>
    public async Task<bool> RemovePostInstallationActionAsync(PreOobeSettings expected, string actionId, PreOobePackageLibraryService library)
    {
        using var suspension = SuspendActivation();
        await gate.WaitAsync(lifetime.Token);
        try
        {
            if (!ReferenceEquals(expected, configuration.Current.PreOobe))
                throw new InvalidOperationException("The post-installation configuration changed before removal.");
            PreOobeActionSettings action = expected.Actions.Single(action => action.Id == actionId);
            configuration.UpdatePreOobe(expected with { Actions = expected.Actions.Where(item => item.Id != actionId).ToArray() }, requirePersistence: true);
            if (action.Package is not { } package) return true;

            Logger.Information("Post-installation package cleanup started. ActionId={ActionId}, ContentHash={ContentHash}", actionId, package.ContentHash);
            try
            {
                long version = editVersion;
                PreOobeSettings remaining = configuration.Current.PreOobe;
                if (Active is not null && editVersion != persistedEditVersion) await SaveCurrentAsync();
                using var references = await Task.Run(local.AcquirePostInstallationPackageReferences, lifetime.Token);
                if (editVersion != version || !ReferenceEquals(remaining, configuration.Current.PreOobe))
                    throw new InvalidOperationException("The configuration changed during package cleanup.");

                var hashes = new HashSet<string>(references.ContentHashes, StringComparer.OrdinalIgnoreCase);
                foreach (PreOobeActionSettings item in remaining.Actions)
                    if (item.Package is { } referenced) hashes.Add(referenced.ContentHash);
                if (hashes.Contains(package.ContentHash))
                {
                    Logger.Information("Post-installation package retained because it is referenced. ContentHash={ContentHash}", package.ContentHash);
                    return true;
                }

                await library.DeleteAsync(package.ContentHash, hashes, lifetime.Token);
                Logger.Information("Post-installation package cleanup completed. ContentHash={ContentHash}", package.ContentHash);
                return true;
            }
            catch (Exception exception)
            {
                Logger.Warning(exception, "Post-installation action removed but package cleanup could not complete. ActionId={ActionId}, ContentHash={ContentHash}", actionId, package.ContentHash);
                return false;
            }
        }
        finally { gate.Release(); }
    }
}
